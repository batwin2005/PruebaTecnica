using PruebaTecnica.Interfaces;
using PruebaTecnica.Models;

namespace PruebaTecnica.Data
{

        public class OrderRepository : IOrderRepository
        {
            private readonly MyDbContext _context;

            public OrderRepository(MyDbContext context)
            {
                _context = context;
            }

            public async Task<Order?> GetOrderByIdAsync(string orderId)
            {
                return await _context.Orders.FindAsync(orderId);
            }

            public async Task UpdateOrderStatusAsync(string orderId, string status)
            {
                var order = await _context.Orders.FindAsync(orderId);
                if (order != null)
                {
                    order.Status = status;
                    await _context.SaveChangesAsync();
                }
            }
        }

   
}
