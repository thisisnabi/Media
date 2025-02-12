using MassTransit;
using Media.Infrastructure;
using Media.Infrastructure.IntegrationEvents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<MediaDbContext>(configure =>
{
    configure.UseInMemoryDatabase("MediaDb");
});

var endpoint = builder.Configuration["MinioStorage:MinioEndpoint"];
var accessKey = builder.Configuration["MinioStorage:AccessKey"];
var secretKey = builder.Configuration["MinioStorage:SecretKey"];

builder.Services.AddMinio(configureClient => configureClient
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(false)
            .Build());

builder.BrokerConfiure();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/{bucket_name}/{catalog_id}", async (
    [FromRoute(Name = "bucket_name")] string bucketName,
    [FromRoute(Name = "catalog_id")] string catalogId,
    IFormFile file,
    MediaDbContext dbContext,
    IPublishEndpoint publisher,
    IMinioClient minioClient) =>
{
    var putObjectArgs = new PutObjectArgs()
                                .WithBucket(bucketName)
                                .WithObject(file.FileName)
                                .WithContentType(file.ContentType)
                                .WithStreamData(file.OpenReadStream())
                                .WithObjectSize(file.Length);

    try
    {

        await minioClient.PutObjectAsync(putObjectArgs);

        var token = new UrlToken
        {
            BucketName = bucketName,
            ObjectName = file.FileName,
            ContentType = file.ContentType,
            ExpireOn = DateTime.UtcNow.AddMinutes(10),
            Id = Guid.NewGuid()
        };

        dbContext.Tokens.Add(token);
        await dbContext.SaveChangesAsync();

        var url = $"https://localhost:7030/{token.Id}";
        await publisher.Publish(new MediaUploadedEvent(file.FileName, url, catalogId, DateTime.UtcNow));
    }
    catch (Exception)
    {

    }

    // remove on production
}).DisableAntiforgery();


app.MapGet("/{token:guid:required}", async (
    Guid token,
    MediaDbContext dbContext,
    IMinioClient minioClient,
    HttpContext httpContext) =>
{
    var foundToken = await dbContext.Tokens.FirstOrDefaultAsync(x => x.Id == token && x.ExpireOn >= DateTime.UtcNow);

    if (foundToken is null)
        throw new InvalidOperationException();

    foundToken.CountAccess++;
    await dbContext.SaveChangesAsync();
    
    GetObjectArgs getObjectArgs = new GetObjectArgs()
                               .WithBucket(foundToken.BucketName)
                               .WithObject(foundToken.ObjectName)
                               .WithCallbackStream( async (stream, cancellationToken) =>
                               {
                                   httpContext.Response.ContentType = foundToken.ContentType;
                                   await stream.CopyToAsync(httpContext.Response.Body, cancellationToken);
                               });
    
    await minioClient.GetObjectAsync(getObjectArgs);
    
    return Results.Empty;

});

app.Run();
