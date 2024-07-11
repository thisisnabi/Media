using MassTransit;
using Media.Infrastructure;
using Media.Infrastructure.IntegrationEvents;
using Microsoft.AspNetCore.Mvc;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.BrokerConfiure();

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/{backet_name}/{catalog_id}", async (
    [FromRoute(Name = "backet_name")] string backetName,
    [FromRoute(Name = "catalog_id")] string catalogId,
    IFormFile file,
    IPublishEndpoint publisher,
    IConfiguration configuration) =>
{
    var endpoint = configuration["MinioStorage:MinioEndpoint"];
    var accessKey = configuration["MinioStorage:AccessKey"];
    var secretKey = configuration["MinioStorage:SecretKey"];
    var minio = new MinioClient()
                        .WithEndpoint(endpoint)
                        .WithCredentials(accessKey, secretKey)
                        .Build();


    var putObjectArgs = new PutObjectArgs()
                                .WithBucket(backetName)
                                .WithObject(file.FileName)
                                .WithContentType(file.ContentType)
                                .WithStreamData(file.OpenReadStream())
                                .WithObjectSize(file.Length);

    try
    {

        await minio.PutObjectAsync(putObjectArgs);
        var url = $"{endpoint}/{backetName}/{file.FileName}";
        await publisher.Publish(new MediaUploadedEvent(file.FileName, url, catalogId, DateTime.UtcNow));
    }
    catch (Exception)
    {

    }


}).DisableAntiforgery();

app.Run();
