using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.DynamoDBv2;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using ExternalProcessor.Simulator;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IAmazonDynamoDB, AmazonDynamoDBClient>();
builder.Services.AddSingleton(_ => DynamoDbOptions.FromEnvironment());
builder.Services.AddSingleton<IProcessorOperationRepository, DynamoDbProcessorOperationRepository>();
builder.Services.AddSingleton<SubmissionService>();

var app = builder.Build();
var submissionLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SimulatorSubmission");

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .ExcludeFromDescription();

app.MapPost(
        "/v1/transactions",
        async (
            SubmitTransactionRequest request,
            HttpRequest httpRequest,
            SubmissionService service,
            CancellationToken cancellationToken) =>
        {
            var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString();
            var correlationValue = httpRequest.Headers["X-Correlation-Id"].ToString();
            var correlationId = Guid.TryParse(correlationValue, out var parsedCorrelationId)
                ? parsedCorrelationId
                : Guid.Empty;

            try
            {
                var response = await service.SubmitAsync(
                    request,
                    idempotencyKey,
                    correlationId,
                    cancellationToken);
                SubmissionLog.Handled(submissionLogger, "SimulatorSubmissionHandled", 202,
                    response.TransactionId, correlationId, response.ProcessorTransactionId);
                return Results.Accepted(value: response);
            }
            catch (SubmissionValidationException exception)
            {
                SubmissionLog.Handled(submissionLogger, "SimulatorSubmissionHandled", 400,
                    request.TransactionId, correlationId, null);
                return Results.BadRequest(
                    new ErrorResponse(
                        "VALIDATION_ERROR",
                        exception.Message,
                        exception.Errors));
            }
            catch (SubmissionIdempotencyConflictException exception)
            {
                SubmissionLog.Handled(submissionLogger, "SimulatorSubmissionHandled", 409,
                    request.TransactionId, correlationId, null);
                return Results.Conflict(
                    new ErrorResponse("IDEMPOTENCY_CONFLICT", exception.Message));
            }
            catch (SimulatedSubmissionTimeoutException exception)
            {
                SubmissionLog.SimulatedTimeout(submissionLogger, request.TransactionId, correlationId);
                SubmissionLog.Handled(submissionLogger, "SimulatorSubmissionHandled", 503,
                    request.TransactionId, correlationId, null);
                return Results.Json(
                    new ErrorResponse("SIMULATED_TIMEOUT", exception.Message),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception exception)
            {
                if (submissionLogger.IsEnabled(LogLevel.Error))
                {
                    var errorType = exception.GetType().Name;
                    var causeType = exception.GetBaseException().GetType().Name;
                    var stackTrace = exception.StackTrace;
                    SubmissionLog.Failed(submissionLogger, "SimulatorSubmissionFailed",
                        request.TransactionId, correlationId, errorType, causeType, stackTrace);
                }

                // Keep the server failure, but do not expose provider messages to runtime logs.
                throw new InvalidOperationException("Simulator submission failed; inspect the correlated failure event.");
            }
        })
    .Accepts<SubmitTransactionRequest>("application/json")
    .Produces<AcceptedTransactionResponse>(StatusCodes.Status202Accepted)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
    .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
    .WithName("SubmitExternalTransaction");

app.Run();

public partial class Program;
