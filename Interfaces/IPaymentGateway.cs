namespace PruebaTecnica.Interfaces
{
    public interface IPaymentGateway
    {
        Task<bool> ChargeAsync(decimal amount);
    }

}
