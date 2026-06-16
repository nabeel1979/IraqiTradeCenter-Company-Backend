using IraqiTradeCenterCompany.Modules.Inventory.Application.Features.CreateItem;
using IraqiTradeCenterCompany.Modules.Inventory.Application.Features.GetItemsList;
using IraqiTradeCenterCompany.Modules.Inventory.Application.Features.RecordStockMovement;
using IraqiTradeCenterCompany.Modules.Inventory.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.API.Controllers;

public class ItemsController : BaseApiController
{
    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif"];
    private const long MaxImageBytes = 5 * 1024 * 1024; // 5 MB

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateItemCommand cmd)
        => HandleResult(await Mediator.Send(cmd));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] int? categoryId = null, [FromQuery] bool? lowStock = null)
    {
        var data = await Mediator.Send(new GetItemsListQuery(pageNumber, pageSize, search, categoryId, lowStock));
        return Ok(new { success = true, data });
    }

    [HttpPost("stock-movements")]
    public async Task<IActionResult> RecordMovement([FromBody] RecordStockMovementCommand cmd)
        => HandleResult(await Mediator.Send(cmd));

    // POST /api/items/{id}/image
    [HttpPost("{id:int}/image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImage(
        int id,
        IFormFile file,
        [FromServices] IInventoryDbContext db,
        [FromServices] IAttachmentStorage storage,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "لم يتم إرسال ملف" });
        if (file.Length > MaxImageBytes)
            return BadRequest(new { message = "حجم الصورة يتجاوز 5 ميغابايت" });
        if (!AllowedImageTypes.Contains(file.ContentType.ToLowerInvariant()))
            return BadRequest(new { message = "نوع الملف غير مدعوم. المسموح: jpg، png، webp، gif" });

        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound(new { message = "الصنف غير موجود" });

        // حذف الصورة القديمة إن وجدت
        if (!string.IsNullOrWhiteSpace(item.MainImageStorageKey))
        {
            try { await storage.DeleteAsync(item.MainImageStorageKey, ct); } catch { /* تُتجاهل */ }
        }

        await using var stream = file.OpenReadStream();
        var key = await storage.SaveAsync($"items/{id}", file.FileName, stream, file.ContentType, ct);

        item.SetMainImage(key);
        await db.SaveChangesAsync(ct);

        return Ok(new { success = true, message = "تم رفع الصورة بنجاح" });
    }

    // DELETE /api/items/{id}/image
    [HttpDelete("{id:int}/image")]
    public async Task<IActionResult> DeleteImage(
        int id,
        [FromServices] IInventoryDbContext db,
        [FromServices] IAttachmentStorage storage,
        CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound(new { message = "الصنف غير موجود" });

        if (!string.IsNullOrWhiteSpace(item.MainImageStorageKey))
        {
            try { await storage.DeleteAsync(item.MainImageStorageKey, ct); } catch { /* تُتجاهل */ }
        }

        item.RemoveMainImage();
        await db.SaveChangesAsync(ct);

        return Ok(new { success = true, message = "تم حذف الصورة" });
    }

    // GET /api/items/{id}/image  — لعرض الصورة في لوحة الشركة
    [HttpGet("{id:int}/image")]
    public async Task<IActionResult> GetImage(
        int id,
        [FromServices] IInventoryDbContext db,
        [FromServices] IAttachmentStorage storage,
        CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null || string.IsNullOrWhiteSpace(item.MainImageStorageKey))
            return NotFound();

        try
        {
            var stream = await storage.OpenReadAsync(item.MainImageStorageKey, ct);
            var ext = Path.GetExtension(item.MainImageStorageKey).ToLowerInvariant();
            var contentType = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
            Response.Headers["Cache-Control"] = "public, max-age=86400";
            return File(stream, contentType);
        }
        catch
        {
            return NotFound();
        }
    }
}
