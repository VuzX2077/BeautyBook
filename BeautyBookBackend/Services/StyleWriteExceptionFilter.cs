using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BeautyBookBackend.Services;

public sealed class StyleWriteExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is Microsoft.EntityFrameworkCore.DbUpdateException busy &&
            busy.InnerException is Npgsql.PostgresException { SqlState: "55000" } &&
            busy.Entries.Any(x => x.Entity is Models.Portfolio or Models.PortfolioStyleLink or Models.PortfolioTermLink))
        {
            context.Result = new ConflictObjectResult(new { Code = "PORTFOLIO_WRITE_BUSY", Message = "Hệ thống đang xử lý dữ liệu; vui lòng thử lại." });
            context.ExceptionHandled = true;
            return;
        }
        if (context.Exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException conflict &&
            conflict.Entries.Any(x => x.Entity is Models.Portfolio))
        {
            context.Result = new ConflictObjectResult(new { Code = "PORTFOLIO_VERSION_CONFLICT", Message = "Tác phẩm đã thay đổi; hãy tải lại trước khi lưu." });
            context.ExceptionHandled = true;
            return;
        }
        var catalogAction = context.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor action &&
            (action.ControllerTypeInfo.AsType() == typeof(BeautyBookBackend.Controllers.AdminMakeupCatalogController) ||
             action.ControllerTypeInfo.AsType() == typeof(BeautyBookBackend.Controllers.AdminMakeupStylesController));
        if (catalogAction && context.Exception is PlayReviewOperationException)
        {
            context.Result = new ForbidResult(); context.ExceptionHandled = true; return;
        }
        if (context.Exception is Microsoft.EntityFrameworkCore.DbUpdateException write &&
            write.Entries.Any(x => x.Entity is Models.MakeupStyle or Models.MakeupCatalogTerm or Models.MakeupStyleTermLink or Models.MakeupCatalogChange) &&
            (write is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException || write.InnerException is Npgsql.PostgresException { SqlState: "23505" }))
        {
            context.Result = new ConflictObjectResult(new { Code = "CATALOG_WRITE_CONFLICT", Message = "Dữ liệu đã thay đổi hoặc trùng định danh; vui lòng tải lại." });
            context.ExceptionHandled = true;
            return;
        }
        if (context.Exception is not StyleWriteException error) return;
        context.Result = new ObjectResult(new { error.Code, Message = error.Message, error.StyleIds, error.Services }) { StatusCode = error.Status };
        context.ExceptionHandled = true;
    }
}
