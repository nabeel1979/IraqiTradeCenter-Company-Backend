using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IraqiTradeCenterCompany.API.Attachments;
using IraqiTradeCenterCompany.API.Auth.Permissions;
using IraqiTradeCenterCompany.API.Controllers;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IraqiTradeCenterCompany.API.Settings;

/// <summary>
/// إعدادات مخزن المرفقات (المسار المحلي + مفاتيح R2) قابلة للتعديل من واجهة
/// النظام دون إعادة نشر. تتطلَّب صلاحية <c>System.CompanySettings.Update</c>.
///
/// الـ GET لا يُعيد الـ Secret الكامل (يُقنَّع بنجوم) — يكفي إظهار آخر 4
/// محارف لتأكيد الحفظ. الـ PUT يقبل قيمة فارغة لإبقاء الـ Secret القديم.
/// </summary>
[Route("api/settings/attachments")]
public class AttachmentSettingsController : BaseApiController
{
    private readonly IAttachmentSettingsService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _perms;
    private readonly IAuditLogger _audit;

    private readonly IAttachmentStorageRegistry _storageRegistry;

    public AttachmentSettingsController(
        IAttachmentSettingsService service,
        ICurrentUserService currentUser,
        IPermissionService perms,
        IAuditLogger audit,
        IAttachmentStorageRegistry storageRegistry)
    {
        _service = service;
        _currentUser = currentUser;
        _perms = perms;
        _audit = audit;
        _storageRegistry = storageRegistry;
    }

    public class AttachmentSettingsDto
    {
        public string Provider { get; set; } = "Local";
        public string? LocalRootPath { get; set; }
        public string? R2AccountId { get; set; }
        public string? R2AccessKeyId { get; set; }
        /// <summary>المفتاح السرّي مُقنَّع — يَظهر فقط آخر 4 محارف على شكل <c>****abcd</c>.</summary>
        public string? R2SecretAccessKeyMasked { get; set; }
        /// <summary>هل المفتاح السرّي مُعيَّن فعلياً (لتمييز "غير مُعيَّن" عن "تمّ حفظه").</summary>
        public bool R2SecretAccessKeySet { get; set; }
        public string? R2Bucket { get; set; }
        /// <summary>Endpoint S3 API المُشتق من Account ID (للعرض فقط).</summary>
        public string? R2Endpoint { get; set; }
        /// <summary>default أو eu — يؤثّر على بناء الـ endpoint.</summary>
        public string? R2Jurisdiction { get; set; }
        public string? R2PublicBaseUrl { get; set; }
        public long MaxFileSizeBytes { get; set; }
        public string? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class UpdateAttachmentSettingsRequest
    {
        public string? Provider { get; set; }
        public string? LocalRootPath { get; set; }
        public string? R2AccountId { get; set; }
        public string? R2AccessKeyId { get; set; }
        /// <summary>إن كان <c>null</c> أو فارغاً نُبقي السرّ القديم؛ غير ذلك نستبدله.</summary>
        public string? R2SecretAccessKey { get; set; }
        public string? R2Bucket { get; set; }
        public string? R2Jurisdiction { get; set; }
        public string? R2PublicBaseUrl { get; set; }
        public long? MaxFileSizeBytes { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!await CanReadAsync(ct)) return Forbid();
        var row = await _service.GetAsync(ct);
        return Ok(new { success = true, data = ToDto(row) });
    }

    /// <summary>
    /// لقطة حالة مزامنة المرفقات (للأيقونة في شريط الرأس). متاحة لأي مستخدم
    /// مسجَّل دخوله — لا تكشف بيانات حسّاسة، فقط أرقام الطابور وزمن آخر دورة.
    /// </summary>
    [HttpGet("sync-status")]
    public IActionResult GetSyncStatus()
    {
        var s = AttachmentSyncBackgroundService.Status;
        return Ok(new
        {
            success = true,
            data = new
            {
                lastTickAtUtc = s.LastTickAtUtc?.ToString("o"),
                pendingUploads = s.PendingUploads,
                pendingDeletes = s.PendingDeletes,
                pendingLocalPurge = s.PendingLocalPurge,
                failedCount = s.FailedCount,
                lastUploadedCount = s.LastUploadedCount,
                lastDeletedCount = s.LastDeletedCount,
                lastLocalPurgedCount = s.LastLocalPurgedCount,
                lastWarning = s.LastWarning,
                lastError = s.LastError,
                lastErrorAtUtc = s.LastErrorAtUtc?.ToString("o"),
            }
        });
    }

    /// <summary>
    /// اختبار اتصال مباشر مع Cloudflare R2: نرفع كائناً صغيراً (≤256 بايت) إلى
    /// مفتاح <c>__connectivity_test/{guid}.txt</c>، نتحقّق من نجاح الرفع، ثم
    /// نحذفه فوراً. لا يعتمد على الـ outbox ولا على الـ background service —
    /// يُجري كل شيء في نفس الـ request ليرجع التشخيص الفوري للمستخدم.
    /// </summary>
    [HttpPost("test-r2-connection")]
    public async Task<IActionResult> TestR2Connection(CancellationToken ct)
    {
        if (!await CanReadAsync(ct)) return Forbid();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var row = await _service.GetAsync(ct);
        var checks = new System.Collections.Generic.List<object>();
        var missing = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(row.R2AccountId)) missing.Add("R2AccountId");
        if (string.IsNullOrWhiteSpace(row.R2AccessKeyId)) missing.Add("R2AccessKeyId");
        if (string.IsNullOrWhiteSpace(row.R2SecretAccessKey)) missing.Add("R2SecretAccessKey");
        if (string.IsNullOrWhiteSpace(row.R2Bucket)) missing.Add("R2Bucket");
        if (missing.Count > 0)
        {
            return Ok(new
            {
                success = false,
                stage = "validation",
                message = "إعدادات R2 غير مكتملة: " + string.Join(", ", missing),
                missing,
            });
        }

        var endpointHost = R2EndpointHelper.BuildHost(row.R2AccountId!, row.R2Jurisdiction);
        var endpointUrl = R2EndpointHelper.BuildServiceUrl(row.R2AccountId!, row.R2Jurisdiction);
        var accountIdValid = R2EndpointHelper.IsValidAccountIdFormat(row.R2AccountId);

        var (dnsOk, dnsDetail) = await R2EndpointHelper.ProbeDnsAsync(endpointHost, ct);
        checks.Add(new { stage = "dns", ok = dnsOk, detail = dnsDetail, host = endpointHost });
        if (!dnsOk)
        {
            return Ok(new
            {
                success = false,
                stage = "dns",
                message = "تعذّر حلّ DNS لـ endpoint الحساب.",
                hint = "تحقّق من اتصال السيرفر بالإنترنت وإعدادات DNS.",
                endpoint = endpointUrl,
                endpointHost,
                accountId = row.R2AccountId,
                bucket = row.R2Bucket,
                checks,
                elapsedMs = sw.ElapsedMilliseconds,
            });
        }

        var (tlsGenericOk, tlsGenericDetail) = await R2EndpointHelper.ProbeTlsAsync(
            "r2.cloudflarestorage.com", TimeSpan.FromSeconds(12), ct);
        checks.Add(new { stage = "tls_generic", ok = tlsGenericOk, detail = tlsGenericDetail, host = "r2.cloudflarestorage.com" });

        var (tlsEndpointOk, tlsEndpointDetail) = await R2EndpointHelper.ProbeTlsAsync(
            endpointHost, TimeSpan.FromSeconds(12), ct);
        checks.Add(new { stage = "tls_endpoint", ok = tlsEndpointOk, detail = tlsEndpointDetail, host = endpointHost });

        if (!tlsEndpointOk)
        {
            var hint = BuildTlsEndpointHint(tlsGenericOk, accountIdValid, row.R2Jurisdiction);
            return Ok(new
            {
                success = false,
                stage = "tls_endpoint",
                message = "فشل الاتصال الآمن (TLS) مع endpoint حسابك — قبل الوصول إلى Bucket أو المفاتيح.",
                inner = tlsEndpointDetail,
                hint,
                endpoint = endpointUrl,
                endpointHost,
                accountId = row.R2AccountId,
                accountIdValid,
                bucket = row.R2Bucket,
                jurisdiction = R2EndpointHelper.NormalizeJurisdiction(row.R2Jurisdiction),
                checks,
                elapsedMs = sw.ElapsedMilliseconds,
            });
        }

        var r2 = _storageRegistry.GetByName("R2") as R2AttachmentStorage;
        if (r2 == null)
        {
            return Ok(new { success = false, stage = "registry", message = "لم يتم العثور على مزوّد R2." });
        }

        var key = $"__connectivity_test/{Guid.NewGuid():N}.txt";
        var payload = Encoding.UTF8.GetBytes($"connectivity-test {DateTime.UtcNow:o} from {Environment.MachineName}");

        // ‎مهلة إجمالية صارمة على عملية الاختبار: 60 ثانية كحدّ أقصى — نُلغي الطلب
        // ‎من جانب السيرفر إن تجاوزه، كي لا يبقى المستخدم بانتظار غير محدّد.
        using var hardTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        hardTimeout.CancelAfter(TimeSpan.FromSeconds(60));
        var lct = hardTimeout.Token;

        try
        {
            using (var ms = new MemoryStream(payload))
            {
                await r2.UploadWithKeyDiagnosticAsync(key, ms, "text/plain", lct);
            }
            var uploadMs = sw.ElapsedMilliseconds;

            // ‎التحقق: قراءة الكائن للتأكد من وجوده فعلياً.
            long readBytes = 0;
            using (var stream = await r2.OpenReadDiagnosticAsync(key, lct))
            {
                var buf = new byte[1024];
                int n;
                while ((n = await stream.ReadAsync(buf, 0, buf.Length, lct)) > 0) readBytes += n;
            }
            var readMs = sw.ElapsedMilliseconds - uploadMs;

            await r2.DeleteDiagnosticAsync(key, lct);
            var deleteMs = sw.ElapsedMilliseconds - uploadMs - readMs;
            checks.Add(new { stage = "s3_upload", ok = true, detail = $"{payload.Length} bytes" });
            checks.Add(new { stage = "s3_read", ok = true, detail = $"{readBytes} bytes" });
            checks.Add(new { stage = "s3_delete", ok = true, detail = "ok" });

            return Ok(new
            {
                success = true,
                stage = "complete",
                message = "تم الاتصال مع Cloudflare R2 بنجاح: TLS ✓ رفع ✓ قراءة ✓ حذف ✓",
                endpoint = endpointUrl,
                endpointHost,
                bucket = row.R2Bucket,
                accountId = row.R2AccountId,
                bytesUploaded = payload.Length,
                bytesRead = readBytes,
                checks,
                timings = new
                {
                    uploadMs,
                    readMs,
                    deleteMs,
                    totalMs = sw.ElapsedMilliseconds,
                },
            });
        }
        catch (Exception ex)
        {
            // ‎خاص: أخطاء TLS handshake مع Cloudflare R2.
            var inner = ex.InnerException?.Message;
            var fullMsg = ex.Message + (inner != null ? " // " + inner : "");
            var hint = (string?)null;
            // ‎على Windows Server: HRESULT 0x80072F7D = ERROR_WINHTTP_SECURE_FAILURE.
            var isWinHttpTls = fullMsg.Contains("0x80072F7D")
                || fullMsg.Contains("12175")
                || fullMsg.Contains("WINHTTP_SECURE_FAILURE", StringComparison.OrdinalIgnoreCase);
            if (isWinHttpTls
                || fullMsg.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || fullMsg.Contains("SSPI", StringComparison.OrdinalIgnoreCase)
                || fullMsg.Contains("Handshake", StringComparison.OrdinalIgnoreCase)
                || fullMsg.Contains("Schannel", StringComparison.OrdinalIgnoreCase)
                || fullMsg.Contains("secure channel", StringComparison.OrdinalIgnoreCase))
            {
                hint = BuildTlsEndpointHint(tlsGenericOk, accountIdValid, row.R2Jurisdiction);
            }
            else if (fullMsg.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) || fullMsg.Contains("403", StringComparison.OrdinalIgnoreCase))
            {
                hint = "مفاتيح R2 صحيحة لكن لا تملك صلاحية على الـ Bucket. تحقّق من Permissions في Cloudflare Dashboard.";
            }
            else if (fullMsg.Contains("InvalidAccessKeyId", StringComparison.OrdinalIgnoreCase) || fullMsg.Contains("SignatureDoesNotMatch", StringComparison.OrdinalIgnoreCase))
            {
                hint = "AccessKeyId أو SecretAccessKey غير صحيح.";
            }
            else if (fullMsg.Contains("NoSuchBucket", StringComparison.OrdinalIgnoreCase))
            {
                hint = $"اسم الـ Bucket '{row.R2Bucket}' غير موجود في هذا الحساب — تحقّق من الاسم في لوحة R2.";
            }
            else if (fullMsg.Contains("name or service not known", StringComparison.OrdinalIgnoreCase)
                || fullMsg.Contains("getaddrinfo", StringComparison.OrdinalIgnoreCase)
                || fullMsg.Contains("No such host", StringComparison.OrdinalIgnoreCase))
            {
                hint = "تعذّر حلّ DNS لـ r2.cloudflarestorage.com — تحقّق من اتصال السيرفر بالإنترنت وإعدادات DNS.";
            }
            else if (fullMsg.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || fullMsg.Contains("TaskCanceled", StringComparison.OrdinalIgnoreCase))
            {
                hint = "انتهت المهلة قبل وصول الاستجابة — تحقّق من الـ firewall/proxy على Windows Server (المنفذ 443 الخارج).";
            }

            checks.Add(new { stage = "s3_request", ok = false, detail = ex.Message });

            return Ok(new
            {
                success = false,
                stage = "request",
                message = ex.Message,
                inner,
                hint,
                endpoint = endpointUrl,
                endpointHost,
                bucket = row.R2Bucket,
                accountId = row.R2AccountId,
                accountIdValid,
                checks,
                elapsedMs = sw.ElapsedMilliseconds,
            });
        }
    }

    private static string BuildTlsEndpointHint(bool tlsGenericOk, bool accountIdValid, string? jurisdiction)
    {
        var j = R2EndpointHelper.NormalizeJurisdiction(jurisdiction);
        var parts = new System.Collections.Generic.List<string>
        {
            "فشل TLS مع endpoint حسابك تحديداً — قبل أي طلب S3 (Bucket والمفاتيح لم تُختبر بعد).",
        };
        if (tlsGenericOk)
            parts.Add("اتصال r2.cloudflarestorage.com العام ناجح من نفس السيرفر، لذا المشكلة ليست firewall عاماً.");
        if (!accountIdValid)
            parts.Add("Account ID لا يبدو بصيغة Cloudflare الصحيحة (32 حرف hex) — انسخه من R2 Overview في لوحة Cloudflare (ليس Access Key ولا Bucket).");
        if (j == R2EndpointHelper.EuJurisdiction)
            parts.Add("إن كان الـ Bucket في اختصاص EU جرّب jurisdiction=eu؛ وإن كان عالمياً اختر default.");
        parts.Add("إن كان Account ID صحيحاً: endpoint الحساب قد لا يكون جاهزاً على Cloudflare بعد — انتظر ساعات أو افتح تذكرة دعم Cloudflare لإعادة تهيئة TLS.");
        return string.Join(" ", parts);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateAttachmentSettingsRequest req, CancellationToken ct)
    {
        if (!await CanWriteAsync(ct)) return Forbid();
        if (req == null) return BadRequest(new { success = false, message = "Empty payload" });

        // ‎تحقّق سريع: لو provider=Local يجب أن يكون المسار قابلاً للكتابة (نتحقق
        // ‎بمحاولة إنشائه عند الحفظ — هنا فقط نمنع القيم الفارغة الواضحة).
        var newProvider = string.IsNullOrWhiteSpace(req.Provider) ? null : req.Provider!.Trim();
        if (newProvider != null
            && !string.Equals(newProvider, "Local", System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(newProvider, "R2", System.StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { success = false, message = "Provider must be 'Local' or 'R2'." });
        }

        var by = _currentUser.FullName ?? _currentUser.UserId?.ToString();
        var saved = await _service.UpdateAsync(row =>
        {
            if (newProvider != null) row.Provider = newProvider;
            if (req.LocalRootPath != null) row.LocalRootPath = string.IsNullOrWhiteSpace(req.LocalRootPath) ? null : req.LocalRootPath.Trim();
            if (req.R2AccountId != null) row.R2AccountId = NullIfEmpty(NormalizeR2AccountId(req.R2AccountId));
            if (req.R2AccessKeyId != null) row.R2AccessKeyId = NullIfEmpty(req.R2AccessKeyId);
            // ‎السرّ: فارغ ⇒ أبقِ القديم. غير فارغ ⇒ استبدل.
            if (!string.IsNullOrWhiteSpace(req.R2SecretAccessKey)) row.R2SecretAccessKey = req.R2SecretAccessKey!.Trim();
            if (req.R2Bucket != null) row.R2Bucket = NullIfEmpty(req.R2Bucket);
            if (req.R2Jurisdiction != null)
                row.R2Jurisdiction = R2EndpointHelper.NormalizeJurisdiction(req.R2Jurisdiction);
            if (req.R2PublicBaseUrl != null) row.R2PublicBaseUrl = NullIfEmpty(req.R2PublicBaseUrl);
            if (req.MaxFileSizeBytes.HasValue && req.MaxFileSizeBytes.Value > 0) row.MaxFileSizeBytes = req.MaxFileSizeBytes.Value;
        }, by, ct);

        // ‎للمزوّد المحلي: حاول إنشاء المجلد كي يفشل الحفظ مبكراً إن لم يكن متاحاً.
        if (string.Equals(saved.Provider, "Local", System.StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(saved.LocalRootPath))
        {
            try { Directory.CreateDirectory(saved.LocalRootPath!); }
            catch (System.Exception ex)
            {
                return Ok(new
                {
                    success = true,
                    warning = $"تم الحفظ لكن لم نتمكن من إنشاء المجلد المحلي: {ex.Message}",
                    data = ToDto(saved),
                });
            }
        }

        await _audit.LogAsync(
            entityType: "AttachmentStorageSettings",
            entityId: "1",
            action: AuditActions.Update,
            summary: "تحديث إعدادات مخزن المرفقات",
            details: new
            {
                provider = saved.Provider,
                hasLocalRoot = !string.IsNullOrWhiteSpace(saved.LocalRootPath),
                hasR2 = !string.IsNullOrWhiteSpace(saved.R2Bucket),
                maxFileSizeBytes = saved.MaxFileSizeBytes,
            },
            ct: ct);

        return Ok(new { success = true, data = ToDto(saved) });
    }

    private async Task<bool> CanReadAsync(CancellationToken ct)
    {
        if (_currentUser.IsSuperAdmin) return true;
        var uid = _currentUser.UserId;
        if (uid is null) return false;
        return await _perms.HasPermissionAsync(uid.Value, PermissionRegistry.System.CompanySettings.Read, ct)
            || await _perms.HasPermissionAsync(uid.Value, PermissionRegistry.System.CompanySettings.Update, ct);
    }

    private async Task<bool> CanWriteAsync(CancellationToken ct)
    {
        if (_currentUser.IsSuperAdmin) return true;
        var uid = _currentUser.UserId;
        if (uid is null) return false;
        return await _perms.HasPermissionAsync(uid.Value, PermissionRegistry.System.CompanySettings.Update, ct);
    }

    private static AttachmentSettingsDto ToDto(AttachmentStorageSettings row) => new()
    {
        Provider = row.Provider,
        LocalRootPath = row.LocalRootPath,
        R2AccountId = row.R2AccountId,
        R2AccessKeyId = row.R2AccessKeyId,
        R2SecretAccessKeyMasked = Mask(row.R2SecretAccessKey),
        R2SecretAccessKeySet = !string.IsNullOrWhiteSpace(row.R2SecretAccessKey),
        R2Bucket = row.R2Bucket,
        R2Jurisdiction = R2EndpointHelper.NormalizeJurisdiction(row.R2Jurisdiction),
        R2Endpoint = string.IsNullOrWhiteSpace(row.R2AccountId)
            ? null
            : R2EndpointHelper.BuildServiceUrl(row.R2AccountId, row.R2Jurisdiction),
        R2PublicBaseUrl = row.R2PublicBaseUrl,
        MaxFileSizeBytes = row.MaxFileSizeBytes,
        UpdatedAtUtc = row.UpdatedAtUtc?.ToString("o"),
        UpdatedBy = row.UpdatedBy,
    };

    private static string? Mask(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return null;
        if (secret.Length <= 4) return new string('*', secret.Length);
        return new string('*', System.Math.Min(secret.Length - 4, 12)) + secret[^4..];
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>يستخرج Account ID (32 hex) إذا لُصق URL كامل بالخطأ.</summary>
    private static string? NormalizeR2AccountId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            s = s[(s.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var m = System.Text.RegularExpressions.Regex.Match(s, @"([0-9a-fA-F]{32})\.r2\.cloudflarestorage\.com", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[0-9a-fA-F]{32}$"))
            return s.ToLowerInvariant();
        return s;
    }
}
