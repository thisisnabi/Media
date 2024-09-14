using Microsoft.EntityFrameworkCore;

namespace Media.Infrastructure
{
     
    public class MediaDbContext : DbContext
    {
        public MediaDbContext(DbContextOptions<MediaDbContext> options) : base(options) 
        {
            
        }

        public DbSet<UrlToken> Tokens { get; set; }
    }
}


public class UrlToken
{
    public Guid Id { get; set; }

    public required string BacketName { get; set; }

    public required string ObjectName { get; set; }

    public required string ContentType { get; set; }

    public DateTime ExpireOn { get; set; }

    public int CountAccess { get; set; }
    public int LimitationAccess { get; set; }

}