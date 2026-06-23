using Microsoft.EntityFrameworkCore;

namespace ContosoUniversity.Data.DbContexts
{
    public class ApiContext : DbContext
    {
        public ApiContext(DbContextOptions<ApiContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            string schema = "Contoso";

            var config = new DbContextConfig();
            config.ApplicationContextConfig(modelBuilder, schema);
        }
    }
}
