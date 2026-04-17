using PruebaTecnica.Interfaces;

namespace PruebaTecnica.Services
{
    public class PaymentGateway : IPaymentGateway
    {
        public async Task<bool> ChargeAsync(decimal amount)
        {
            await Task.Delay(500); // Simula procesamiento
            return amount > 0;
        }
    }

}
