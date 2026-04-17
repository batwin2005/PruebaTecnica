
INGENIERO DARWIN AGUIÑO
CALI COLOMBIA
PRUEBA DE CONOCIMIENTO

----------------------------------------------------------------------------------------------------------------------------------------------------------------------
SECCION 1 : 
______________________________________________________________________________________________________________________________________________________________________
DE ACORDE AL CODIGO LO QUE ENCONTRE FUE LO SIGUIENTE: 


1) 
- Inyección SQL: las consultas directas de el SQL en el codigo, permitiendo SQL injection.
- Credenciales en código: la cadena de conexión contiene usuario y contraseña en texto plano dentro del código fuente.
- Bloqueo/sincronía y recursos mal gestionados: uso síncrono de I/O (bloqueante) y db.Close() fuera de finally, lo que puede dejar conexiones abiertas en caso de excepción.
- Acoplamiento fuerte y violación de SRP: OrderProcessor hace acceso a BD, cobro, actualización y envío de email; una clase tiene demasiadas responsabilidades.
- Falta de manejo de errores y transacciones: no hay transacción para asegurar atomicidad entre cobro y actualización de estado; si falla la actualización o el email, el pago ya pudo haberse procesado.
- Falta de validación y tipado de datos: reader["Amount"] y reader["Email"] se usan sin validación ni conversión segura; posible excepción o comportamiento inesperado.
- Dependencias concretas y difícil de testear: se crean instancias concretas (new PaymentGateway(), new SmtpClient(...)) dentro del método, impidiendo pruebas unitarias y mocks.
- Posible fuga de información y seguridad en email: envío de emails sin autenticación segura ni manejo de errores; además no se respeta privacidad ni logs seguros.

2)

CODIGO RESCRIO SEGUN MI CRITERIO: 

using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

// Modelos
public record Order(
    string Id,
    long AmountCents,
    string Currency,
    string CustomerEmail,
    OrderStatus Status
);

public enum OrderStatus { Pending, Paid, Failed }

// Abstracciones (interfaces) - Dependency Inversion and Interface Segregation
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string orderId, CancellationToken ct = default);
    Task MarkPaidAsync(string orderId, TransactionContext? tx = null, CancellationToken ct = default);
}

public interface IPaymentGateway
{
    /// <summary>
    /// Realiza el cargo de forma idempotente usando un idempotencyKey.
    /// </summary>
    Task<PaymentResult> ChargeAsync(long amountCents, string currency, PaymentMetadata metadata, CancellationToken ct = default);
}

public interface IEmailService
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

public interface ITransactionManager
{
    /// <summary>
    /// Ejecuta la función dentro de una transacción y la confirma o revierte según el resultado.
    /// </summary>
    Task<T> RunInTransactionAsync<T>(Func<TransactionContext, Task<T>> action, CancellationToken ct = default);
}

public interface ILogger
{
    void Info(string message, object? meta = null);
    void Warn(string message, object? meta = null);
    void Error(string message, Exception? ex = null, object? meta = null);
}

// Tipos auxiliares
public sealed class TransactionContext
{
    // Representa el handle de la transacción del proveedor (DB, ORM, etc.)
}

public record PaymentMetadata(string OrderId, string? CustomerEmail);
public record PaymentResult(bool Success, string? TransactionId, string? ErrorCode);

// OrderProcessor aplicando SRP y DI
public class OrderProcessor
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IEmailService _emailService;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger _logger;

    public OrderProcessor(
        IOrderRepository orderRepository,
        IPaymentGateway paymentGateway,
        IEmailService emailService,
        ITransactionManager transactionManager,
        ILogger logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _paymentGateway = paymentGateway ?? throw new ArgumentNullException(nameof(paymentGateway));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Procesa la orden de forma asíncrona. Valida, cobra, actualiza estado y notifica.
    /// </summary>
    public async Task ProcessOrderAsync(string orderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            _logger.Warn("OrderId inválido", new { orderId });
            throw new ArgumentException("orderId is required", nameof(orderId));
        }

        _logger.Info("Iniciando procesamiento de orden", new { orderId });

        // 1. Leer orden (lectura fuera de transacción para minimizar locks)
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            _logger.Warn("Orden no encontrada", new { orderId });
            throw new InvalidOperationException("Order not found");
        }

        if (order.Status == OrderStatus.Paid)
        {
            _logger.Info("Orden ya pagada, no se procesa", new { orderId });
            return;
        }

        // 2. Ejecutar cobro y actualización de estado con control de transacción
        try
        {
            // Ejecutamos la actualización de estado dentro de una transacción.
            // El cargo a la pasarela puede ser externo; se recomienda idempotencia para evitar cargos duplicados.
            await _transactionManager.RunInTransactionAsync(async tx =>
            {
                // 2.a Cobro (asegurar idempotencia con orderId como key)
                var paymentMeta = new PaymentMetadata(order.Id, order.CustomerEmail);
                var paymentResult = await _paymentGateway.ChargeAsync(order.AmountCents, order.Currency, paymentMeta, ct);

                if (!paymentResult.Success)
                {
                    _logger.Warn("Cobro fallido", new { orderId, paymentResult.ErrorCode });
                    throw new InvalidOperationException("Payment failed");
                }

                // 2.b Actualizar estado en BD dentro de la transacción
                await _orderRepository.MarkPaidAsync(order.Id, tx, ct);

                // Retornamos un valor cualquiera; la transacción se confirmará si no hay excepción.
                return true;
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.Error("Error durante cobro/actualización", ex, new { orderId });
            throw;
        }

        // 3. Enviar email fuera de la transacción para no bloquear el commit por fallos en SMTP
        try
        {
            var subject = "Pago exitoso";
            var body = $"Tu orden {order.Id} ha sido pagada correctamente.";
            await _emailService.SendAsync(order.CustomerEmail, subject, body, ct);
        }
        catch (Exception ex)
        {
            // Loguear y opcionalmente encolar reintento; no revertimos el pago por fallo en notificación.
            _logger.Error("Fallo al enviar email de confirmación", ex, new { orderId });
            // Aquí podrías encolar un reintento en un sistema de mensajería.
        }

        _logger.Info("Procesamiento de orden finalizado", new { orderId });
    }
}




---------------------------------------------------------------------------------------------------------------------------------------------------------------------
SECCION 2:
______________________________________________________________________________________________________________________________________________________________________

DIAGRAMA ARQUITECTURA : 

[Cliente Web/Móvil]
        |
        v
  [CloudFront / WAF]
        |
        v
   [API Gateway]  <---> [Auth (Cognito / OIDC)]
        |
        v
  +-----------------------------+
  |  Capa de Entrada (stateless)|
  |  - Lambda (validación rápida)|
  |  - ALB -> Fargate (ECS) para|
  |    procesos más largos       |
  +-----------------------------+
        |
        v
  [Servicio Disponibilidad]  <-->  [ElastiCache Redis] (caché + locks)
        |
        v
  [Servicio Reservas]  (crea reserva temporal, TTL 10min)
        |
        v
  [Cola de Mensajes SQS / EventBridge]  <--- DLQ
        |
        +--> [Servicio Pagos] (Fargate / Lambda según latencia)
        |        |
        |        v
        |    [RDS PostgreSQL] (transacciones de pago/orden)
        |        |
        |        v
        |    Publica evento "pago_confirmado" -> SNS / SQS / EventBridge
        |
        +--> [Timeout/Expiración] (Lambda programada o DynamoDB TTL)
        |
        v
  [Servicio Generación PDF] (consume evento pago_confirmado)
        |
        v
  [S3 (almacena PDF)]  -->  [SES (envío email)] / [SNS para notificaciones]
        |
        v
   [Monitoreo: CloudWatch, X-Ray, Alarms]



OPCION 1) 

Pregunta 1: Imagina que estamos construyendo el backend... ¿Qué servicios de cómputo de AWS (EC2, Lambda, ECS, etc.) usarías para absorber el pico de tráfico y por qué?

Respuesta 1:
- API Gateway + AWS Lambda para la capa pública que valida disponibilidad y crea reservas temporales: serverless escala automáticamente con picos y reduce gestión de servidores. Lambda es ideal para ráfagas cortas y muchas peticiones por segundo.
- Amazon ECS/Fargate (contenedores sin gestionar) para lógica más pesada o estado corto (ej. orquestar pagos, llamadas a pasarelas externas) cuando necesitas control de dependencias o tiempos de ejecución más largos.
- Auto Scaling Groups con EC2 solo para cargas muy específicas que requieren hardware dedicado (por ejemplo, procesamiento masivo de PDFs si no se usa un servicio gestionado).
- CloudFront delante de APIs y assets para reducir latencia global y absorber tráfico.
Importante: combinar Lambda para ráfagas y Fargate para procesos largos da flexibilidad y costo-eficiencia.

Pregunta 2: ¿Cómo separarías esto en Microservicios? (Menciona al menos 3 servicios que crearías).

Respuesta 2:
- Servicio de Disponibilidad / Asientos: consulta rápida de asientos, marca reserva temporal (10 min). Debe ser rápido y consistente.
- Servicio de Reservas y Expiración: crea reserva temporal, programa expiración (10 min) y libera asiento si no hay pago. Usa Redis (ElastiCache) o TTL en DB para expiraciones.
- Servicio de Pagos: procesa pago con pasarela externa; idempotente y registra estado.
- Servicio de Generación de Tickets (PDF) y Envío: consume eventos de pago confirmado y genera PDF + envía correo.
- Servicio de Notificaciones / Email: envío de correos y reintentos.
Cada microservicio puede desplegarse en Lambda o Fargate según duración y dependencias. Importante: diseñar APIs pequeñas y contratos claros.

Pregunta 3: ¿En qué caso usarías una base de datos relacional y en cuál una NoSQL?

Respuesta 3:
- Relacional (RDS PostgreSQL): para transacciones críticas: ventas, pagos, historial de órdenes, integridad referencial y consultas ACID (ej. confirmar que un pago corresponde a una reserva). Usar RDS cuando necesitas transacciones y joins.
- NoSQL (DynamoDB): para consultas de alta velocidad y escala: disponibilidad de asientos, sesiones, caché de vistas de evento; permite latencia baja y escalado masivo sin esquemas rígidos. Usar TTL para reservas temporales si conviene.

Pregunta 4: ¿Cómo manejarías la comunicación asíncrona entre el servicio de pagos y el servicio de generación de PDFs?

Respuesta 4:
- Publicar evento "pago_confirmado" en Amazon SQS o SNS + SQS; el servicio de PDFs consume la cola y procesa en segundo plano. Esto desacopla y permite reintentos y escalado independiente.
- Añadir dead-letter queue (DLQ) para errores y trazas (CloudWatch/X-Ray) para observabilidad.
- Opcional: usar EventBridge para rutas más complejas entre servicios.

Riesgos y recomendaciones rápidas: probar carga (stress tests), diseñar idempotencia en pagos, usar caché para lecturas, y monitoreo/alertas.


OPCION 2)

Pregunta 2: ¿Cómo separarías esto en Microservicios? (Menciona al menos 3 servicios que crearías).
Respuesta 2:
- Servicio de Disponibilidad / Asientos: consulta rápida de asientos, marca reserva temporal (10 min). Debe ser rápido y consistente.
- Servicio de Reservas y Expiración: crea reserva temporal, programa expiración (10 min) y libera asiento si no hay pago. Usa Redis (ElastiCache) o TTL en DB para expiraciones.
- Servicio de Pagos: procesa pago con pasarela externa; idempotente y registra estado.
- Servicio de Generación de Tickets (PDF) y Envío: consume eventos de pago confirmado y genera PDF + envía correo.
- Servicio de Notificaciones / Email: envío de correos y reintentos.
- Cada microservicio puede desplegarse en Lambda o Fargate según duración y dependencias. Importante: diseñar APIs pequeñas y contratos claros.
 
OPCION 3) 

Pregunta 3: ¿En qué caso usarías una base de datos relacional y en cuál una NoSQL?
Respuesta 3:
- Relacional (RDS PostgreSQL): para transacciones críticas: ventas, pagos, historial de órdenes, integridad referencial y consultas ACID (ej. confirmar que un pago corresponde a una reserva). Usar RDS cuando necesitas transacciones y joins.
- NoSQL (DynamoDB): para consultas de alta velocidad y escala: disponibilidad de asientos, sesiones, caché de vistas de evento; permite latencia baja y escalado masivo sin esquemas rígidos. Usar TTL para reservas temporales si conviene.


OPCION 4) 

Pregunta 4: ¿Cómo manejarías la comunicación asíncrona entre el servicio de pagos y el servicio de generación de PDFs?
Respuesta 4:
- Publicar evento "pago_confirmado" en Amazon SQS o SNS + SQS; el servicio de PDFs consume la cola y procesa en segundo plano. Esto desacopla y permite reintentos y escalado independiente.
- Añadir dead-letter queue (DLQ) para errores y trazas (CloudWatch/X-Ray) para observabilidad.
- Opcional: usar EventBridge para rutas más complejas entre servicios.



---------------------------------------------------------------------------------------------------------------------------------------------------------------------
SECCION 2:
______________________________________________________________________________________________________________________________________________________________________

Pregunta 1: Transacciones Distribuidas: En una arquitectura de microservicios, si el servicio de "Pagos" falla después de que el servicio de "Inventario" ya descontó el producto, ¿qué patrón de diseño utilizarías para revertir la operación y mantener la consistencia de los datos? Explícalo brevemente.
Respuesta 1:
Usaría el patrón Saga con transacciones compensatorias.
- Qué hace: cada paso (Inventario, Pagos, Pedido) es una transacción local; si un paso posterior falla, se ejecuta una acción compensatoria en los servicios anteriores (por ejemplo: reponer el stock).
- Cómo implementarlo (simple): cuando Inventario descuenta, publica un evento reserva_creada; Pagos intenta cobrar; si Pagos falla, publica pago_fallido y el servicio de Inventario escucha ese evento y ejecuta la compensación reponer_stock.
- Puntos clave: diseñar compensaciones idempotentes (hacer la misma operación varias veces no rompe el estado), usar eventos para orquestar o un orquestador central si se prefiere control único, y aceptar consistencia eventual en lugar de bloqueo global.


Pregunta 2: Ecosistema Híbrido: Si un equipo de la empresa desarrolla un servicio en .NET 8 y otro equipo desarrolla en Node.js, ¿qué estrategias o estándares implementarías (como Arquitecto/Senior) para asegurar que ambos servicios se comuniquen de forma segura, eficiente y que sus APIs sean fáciles de consumir para el Frontend?
Respuesta 2:
Implementaría estas prácticas concretas y simples:
- Contrato claro con OpenAPI (Swagger): definir y versionar las APIs; generar SDKs/clients para .NET y Node.js desde la misma especificación.
- API Gateway + Autenticación estándar: exponer APIs vía API Gateway; usar OAuth2 / OpenID Connect con JWT para autenticación y autorización.
- Seguridad en transporte: HTTPS obligatorio; mTLS si se necesita mayor seguridad entre servicios.
- Contratos y pruebas de contrato: tests automáticos (contract tests) para validar que cambios no rompan consumidores.
- Observabilidad común: logs estructurados, métricas y trazas distribuidas (OpenTelemetry) para ambos stacks.
- Resiliencia y comunicación: timeouts, retries con backoff, circuit breaker y límites de tasa; usar colas/event-bus (SNS/SQS/EventBridge) para operaciones asíncronas.
- Estándares de respuesta: formatos JSON consistentes, códigos HTTP claros, manejo de errores uniforme y documentación de errores.
- CI/CD y contenedores: pipelines que construyan y publiquen imágenes (Docker), pruebas automáticas y despliegue consistente.
- SDKs y librerías compartidas: utilidades comunes (autenticación, validación, modelos) para evitar duplicar lógica.
Con esto el Frontend consume APIs estables y seguras, y los equipos pueden trabajar en sus stacks sin fricciones.






