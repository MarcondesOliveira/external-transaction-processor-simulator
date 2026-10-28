using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.DynamoDBv2;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
                return Results.Accepted(value: response);
            }
            catch (SubmissionValidationException exception)
            {
                return Results.BadRequest(
                    new ErrorResponse(
                        "VALIDATION_ERROR",
                        exception.Message,
                        exception.Errors));
            }
            catch (SubmissionIdempotencyConflictException exception)
            {
                return Results.Conflict(
                    new ErrorResponse("IDEMPOTENCY_CONFLICT", exception.Message));
            }
        })
    .Accepts<SubmitTransactionRequest>("application/json")
    .Produces<AcceptedTransactionResponse>(StatusCodes.Status202Accepted)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
    .WithName("SubmitExternalTransaction");

app.Run();

public partial class Program;
