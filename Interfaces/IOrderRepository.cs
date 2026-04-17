using PruebaTecnica.Models;

namespace PruebaTecnica.Interfaces
{
    public interface IOrderRepository
    {

        Task<Order?> GetOrderByIdAsync(string orderId);
        Task UpdateOrderStatusAsync(string orderId, string status);
    }

}
