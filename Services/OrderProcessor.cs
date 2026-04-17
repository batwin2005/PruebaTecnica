using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PruebaTecnica.Interfaces;
using PruebaTecnica.Models;

namespace PruebaTecnica.Services
{
    public class OrderProcessor : IOrderProcessor
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IPaymentGateway _paymentGateway;
        private readonly IEmailService _emailService;
        private readonly ILogger<OrderProcessor> _logger;

        public OrderProcessor(
            IOrderRepository orderRepository,
            IPaymentGateway paymentGateway,
            IEmailService emailService,
            ILogger<OrderProcessor> logger)
        {
            _orderRepository = orderRepository;
            _paymentGateway = paymentGateway;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task ProcessOrderAsync(string orderId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Iniciando procesamiento de la orden {OrderId}", orderId);

            // Recuperar orden
            var order = await _orderRepository.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Orden no encontrada: {OrderId}", orderId);
                throw new KeyNotFoundException($"Order {orderId} not found");
            }

            // Idempotencia: si ya está pagada, no volver a procesar
            if (string.Equals(order.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Orden {OrderId} ya está en estado PAID. No se procesa de nuevo.", orderId);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            bool isPaid;
            try
            {
                _logger.LogInformation("Intentando cobrar la orden {OrderId} por {Amount}", orderId, order.Amount);
                isPaid = await _paymentGateway.ChargeAsync(order.Amount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar el pago para la orden {OrderId}", orderId);
                throw; // Propagar para que el caller decida (o envolver en excepción de dominio)
            }

            if (!isPaid)
            {
                _logger.LogInformation("Pago rechazado para la orden {OrderId}", orderId);
                return;
            }

            // Actualizar estado de la orden en BD (idealmente en la misma transacción que outbox)
            try
            {
                await _orderRepository.UpdateOrderStatusAsync(order.Id, "PAID");
                _logger.LogInformation("Orden {OrderId} marcada como PAID", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error actualizando el estado de la orden {OrderId}", orderId);
                // Aquí podríamos implementar compensación o reintentos según la política
                throw;
            }

            // Envío de email: no bloquear la confirmación del pago; usar Outbox en producción
            try
            {
                // Recomendado: insertar mensaje en Outbox dentro de la misma transacción que la actualización de la orden.
                await _emailService.SendEmailAsync(order.Email, "Pago exitoso", "Tu orden ha sido pagada.");
                _logger.LogInformation("Email de confirmación enviado para la orden {OrderId}", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo al enviar email para la orden {OrderId}. Se debe reintentar vía Outbox.", orderId);
                // No revertimos el pago; registrar en Outbox o en un mecanismo de reintento
            }
        }
    }
}