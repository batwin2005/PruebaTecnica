namespace PruebaTecnica.Models
{

        public class Order
        {
            public string Id { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public string Email { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }
    
}
