using Microsoft.AspNetCore.Mvc;
using OsService.Application.DTOs;
using OsService.Application.Services;
using OsService.Domain.Enums;

namespace OsService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ServiceOrderController(ServiceOrderAppService appService) : ControllerBase
{
    /// <summary>Cria uma nova Ordem de Serviço.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ServiceOrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateServiceOrderRequest request, CancellationToken ct)
    {
        var result = await appService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>Busca uma OS pelo ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ServiceOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await appService.GetByIdAsync(id, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Lista OSs por status.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ServiceOrderResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByStatus([FromQuery] ServiceOrderStatus status, CancellationToken ct)
    {
        var result = await appService.GetByStatusAsync(status, ct);
        return Ok(result);
    }

    /// <summary>Adiciona item (serviço/peça) à OS durante o diagnóstico.</summary>
    [HttpPost("{id:guid}/items")]
    [ProducesResponseType(typeof(ServiceOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] AddItemRequest request, CancellationToken ct)
    {
        try
        {
            var result = await appService.AddItemAsync(id, request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex)
        {
            return StatusCode(500, new {
                error = ex.Message,
                inner = ex.InnerException?.Message,
                type  = ex.GetType().FullName,
                stack = ex.StackTrace?.Split('\n').Take(8)
            });
        }
    }

    /// <summary>Inicia diagnóstico da OS (Received → InDiagnosis).</summary>
    [HttpPatch("{id:guid}/start-diagnosis")]
    [ProducesResponseType(typeof(ServiceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartDiagnosis(Guid id, CancellationToken ct)
        => await ExecuteStatusChange(() => appService.StartDiagnosisAsync(id, ct));

    /// <summary>
    /// Envia a OS para aprovação de orçamento (InDiagnosis → WaitingApproval).
    /// Publica OsSentForApprovalEvent com os itens diagnosticados —
    /// o BillingService consumirá esse evento e populará o Budget automaticamente.
    /// Requer ao menos um item adicionado à OS.
    /// </summary>
    [HttpPatch("{id:guid}/send-for-approval")]
    [ProducesResponseType(typeof(ServiceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SendForApproval(Guid id, CancellationToken ct)
        => await ExecuteStatusChange(() => appService.SendForApprovalAsync(id, ct));

    /// <summary>
    /// Não inicia a execução pela API. Após BudgetApprovedEvent a OS já vai para InExecution
    /// e publica OsExecutionStartedEvent. Recusar este PATCH evita executar sem orçamento aprovado.
    /// </summary>
    [HttpPatch("{id:guid}/start-execution")]
    [ProducesResponseType(typeof(ServiceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartExecution(Guid id, CancellationToken ct)
        => await ExecuteStatusChange(() => appService.StartExecutionAsync(id, ct));

    /// <summary>
    /// [Override manual] Finaliza o serviço (InExecution → Finished).
    /// No fluxo normal do Saga, este passo será acionado pelo ExecutionCompletedEvent do ExecutionService.
    /// Use este endpoint apenas como override administrativo.
    /// </summary>
    [HttpPatch("{id:guid}/finish")]
    [ProducesResponseType(typeof(ServiceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Finish(Guid id, CancellationToken ct)
        => await ExecuteStatusChange(() => appService.FinishAsync(id, ct));

    /// <summary>
    /// [Override manual] Registra a entrega do veículo ao cliente (Finished → Delivered).
    /// No fluxo normal do Saga, este passo é acionado pelo PaymentConfirmedEvent via handler.
    /// Use este endpoint apenas como override administrativo.
    /// Publica OsClosedEvent.
    /// </summary>
    [HttpPatch("{id:guid}/deliver")]
    [ProducesResponseType(typeof(ServiceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Deliver(Guid id, CancellationToken ct)
        => await ExecuteStatusChange(() => appService.DeliverAsync(id, ct));

    /// <summary>Cancela a OS.</summary>
    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType(typeof(ServiceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
        => await ExecuteStatusChange(() => appService.CancelAsync(id, request.Reason, ct));

    private async Task<IActionResult> ExecuteStatusChange(Func<Task<ServiceOrderResponse>> action)
    {
        try
        {
            var result = await action();
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex)
        {
            return StatusCode(500, new {
                error = ex.Message,
                inner = ex.InnerException?.Message,
                type  = ex.GetType().FullName,
                stack = ex.StackTrace?.Split('\n').Take(8)
            });
        }
    }
}
