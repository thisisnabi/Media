using MassTransit;
using Media.Infrastructure;
using Media.Infrastructure.IntegrationEvents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<MediaDbContext>(configure =>
{
    configure.UseInMemoryDatabase("test");
});

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
    MediaDbContext dbContext,
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

        var token = new UrlToken
        {
            BacketName = backetName,
            ObjectName = file.FileName,
            ContentType = file.ContentType,
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
      MediaDbContext dbContext,
       IConfiguration configuration,
    Guid Token) => {

        var foundToken = await dbContext.Tokens.FirstOrDefaultAsync(x => x.Id == Token);

        if (foundToken is null)
            throw new InvalidOperationException();

        var endpoint = configuration["MinioStorage:MinioEndpoint"];
        var accessKey = configuration["MinioStorage:AccessKey"];
        var secretKey = configuration["MinioStorage:SecretKey"];
        var minio = new MinioClient()
                            .WithEndpoint(endpoint)
                            .WithCredentials(accessKey, secretKey)
                            .Build();

        var memoeyStream = new MemoryStream(); 
        GetObjectArgs getObjectArgs = new GetObjectArgs()
                                   .WithBucket(foundToken.BacketName)
                                   .WithObject(foundToken.ObjectName)
                                   .WithCallbackStream((stream) =>
                                   {
                                       stream.CopyTo(memoeyStream);
                                   });

        await minio.GetObjectAsync(getObjectArgs);
        return Results.File(memoeyStream.ToArray(),contentType: foundToken.ContentType);
    });

app.Run();
