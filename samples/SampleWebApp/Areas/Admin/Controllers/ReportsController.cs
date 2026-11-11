using System.ComponentModel;
using System.Text;
using DynamicAuthorization.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SampleWebApp.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
[DisplayName("Reports")]
public sealed class ReportsController : Controller
{
    // admin.reports.read
    public IActionResult Index() => View();

    // Shares the "orders.export" permission with the export button on the orders page.
    [Permission("orders.export")]
    public IActionResult Export()
    {
        StringBuilder csv = new("Order,Customer,Total\n");
        foreach (SampleOrder order in SampleOrders.All)
        {
            csv.Append(System.Globalization.CultureInfo.InvariantCulture, $"{order.Id},{order.Customer},{order.Total}\n");
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "orders.csv");
    }
}
