using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Shop.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public abstract class AdminController : Controller
{
    protected void Flash(string message, bool error = false)
    {
        TempData["AdminFlash"] = message;
        TempData["AdminFlashError"] = error;
    }
}
