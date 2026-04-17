using System.Threading;
using System.Threading.Tasks;

namespace PruebaTecnica.Interfaces
{
    public interface IOrderProcessor
    {
        Task ProcessOrderAsync(string orderId, CancellationToken cancellationToken = default);
    }
}