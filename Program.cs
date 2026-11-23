using Amazon.Lambda.CloudWatchEvents.ScheduledEvents;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;

// Build DI container once — Lambda reuses this instance across warm invocations
var function = new JobSearchFunction.Function(
    JobSearchFunction.Function.BuildServiceProvider());

// Register handler with the Lambda runtime
await LambdaBootstrapBuilder
    .Create<ScheduledEvent>(function.FunctionHandler, new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();
