using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add the Entity Framework Core DBContext
builder.Services.AddDbContext<PlaceDb>(dbContextOptionsBuilder =>
{
    //dbContextOptionsBuilder.UseSqlServer(builder.Configuration["DefaultConnection"]);
    dbContextOptionsBuilder.UseInMemoryDatabase("BeamLab.Places");
});

// Enable the API explorer
builder.Services.AddEndpointsApiExplorer();

// The OpenAPI description name
var openApiDesc = "BeamLab.Places";

// Add OpenAPI services to the container.
builder.Services.AddSwaggerGen(swaggerGenOptions =>
{
    //swaggerGenOptions.OperationFilter<ImageExtensionFilter>();
    swaggerGenOptions.SwaggerDoc(openApiDesc, new()
    {
        Title = "Places API",
        Version = "2022-01-01"
    });
});

var app = builder.Build();

// Configure for development 
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(swaggerOptions =>
    {
        swaggerOptions.SerializeAsV2 = true;
        swaggerOptions.RouteTemplate = "/{documentName}.json";
    });

    app.UseSwaggerUI(swaggerUiOptions =>
        swaggerUiOptions.SwaggerEndpoint(
            $"/{openApiDesc}.json", openApiDesc)
        );

    // Make sure the SQL DB schema has been created
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<PlaceDb>();
        db.Database.EnsureCreated();
    }

    // Redirect root browses to the swagger page
    app.MapGet("/", () => {
        return Results.Redirect("/swagger");
    });
}

// Enables GET of all jobs
app.MapGet("/places", async (PlaceDb db) =>
        await db.Places.ToListAsync()
    )
    .Produces<List<Place>>(StatusCodes.Status200OK)
    .WithName("GetAllPlaces");

// Enables creation of a new job 
app.MapPost("/places/", async ([FromBody] Place place, [FromServices] PlaceDb db) =>
{
    db.Places.Add(place);
    await db.SaveChangesAsync();

    return Results.Created(
        $"/places/{place.Id}", place);
})
    .Produces<Place>(StatusCodes.Status201Created)
    .WithName("CreatePlace");

// Enables GET of a specific job
app.MapGet("/places/{id}", async (int id, PlaceDb db) =>
        await db.Places.Include("Photos").FirstOrDefaultAsync(place => place.Id == id)
            is Place place
                ? Results.Ok(place)
                : Results.NotFound()
    )
    .Produces<Place>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .WithName("GetPlace");

// Enables searching for a job
app.MapGet("/places/search/{query}", (string query, PlaceDb db) =>
        db.Places
            .Include("Photos")
            .Where(x => x.Name.Contains(query))
            is IEnumerable<Place> places
                ? Results.Ok(places)
                : Results.NotFound(new Place[] { })
    )
    .Produces<List<Place>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .WithName("SearchPlaces");

// Upload a site photo
app.MapPost("/places/{placeId}/photos",
    async (HttpRequest req,
        int placeId,
        BlobServiceClient blobServiceClient,
        PlaceDb db) =>
    {
        if (!req.HasFormContentType) return Results.BadRequest();

        var form = await req.ReadFormAsync();
        var file = form.Files["file"];

        if (file is null) return Results.BadRequest();

        using var upStream = file.OpenReadStream();

        var blobClient = blobServiceClient
               .GetBlobContainerClient("uploads")
                   .GetBlobClient(file.FileName);

        await blobClient.UploadAsync(upStream);

        db.PlacePhotos.Add(new PlacePhoto
        {
            PlaceId = placeId,
            PhotoUploadUrl = blobClient.Uri.AbsoluteUri
        });

        await db.SaveChangesAsync();

        var place = await db.Places.Include("Photos").FirstOrDefaultAsync(x => x.Id == placeId);

        return Results.Created($"/places/{placeId}", place);
    })
    .Produces<Place>(StatusCodes.Status200OK, "application/json");
    //.WithName(ImageExtensionFilter.UPLOAD_SITE_PHOTO_OPERATION_ID);

app.Run();


//Models & DbContext
public class PlacePhoto
{
    public int Id { get; set; }
    public int PlaceId { get; set; }
    public string PhotoUploadUrl { get; set; } = string.Empty;
}

public class Place
{
    public int Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<PlacePhoto> Photos { get; set; } = new List<PlacePhoto>();
}

class PlaceDb : DbContext
{
    public PlaceDb(DbContextOptions<PlaceDb> options) : base(options) { }

    public DbSet<Place> Places => Set<Place>();

    public DbSet<PlacePhoto> PlacePhotos => Set<PlacePhoto>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Place>().HasMany(s => s.Photos);

        base.OnModelCreating(modelBuilder);
    }
}