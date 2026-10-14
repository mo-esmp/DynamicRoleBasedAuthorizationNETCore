using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SampleWebApp.Pages.Orders;

// The display name is what administrators see in the dashboard's permission list.
[DisplayName("Edit orders")]
public sealed class EditModel : PageModel
{
    public SampleOrder Order { get; private set; } = null!;

    public IActionResult OnGet(int id)
    {
        SampleOrder? order = SampleOrders.All.FirstOrDefault(o => o.Id == id);
        if (order is null)
            return NotFound();

        Order = order;
        return Page();
    }
}
