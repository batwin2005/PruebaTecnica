using Microsoft.AspNetCore.Mvc;
using PruebaTecnica.Services;

namespace PruebaTecnica.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderProcessor _orderProcessor;

        [HttpPost("{id}")]
        public async Task<IActionResult> ProcessOrder(string id, CancellationToken cancellationToken)
        {
            try
            {
                await _orderProcessor.ProcessOrderAsync(id, cancellationToken);
                return Ok(new { Message = "Orden procesada correctamente" });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { Message = "Orden no encontrada" });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception)
            {
                return StatusCode(500, new { Message = "Error procesando la orden" });
            }
        }
    }


}
