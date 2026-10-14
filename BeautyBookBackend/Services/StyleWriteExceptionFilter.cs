using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BeautyBookBackend.Services;

public sealed class StyleWriteExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not StyleWriteException error) return;
        context.Result = new ObjectResult(new { error.Code, Message = error.Message, error.StyleIds, error.Services }) { StatusCode = error.Status };
        context.ExceptionHandled = true;
    }
}
