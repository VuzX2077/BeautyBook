using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BeautyBookBackend.Data;

namespace DbCheck {
    class Program {
        static void Main(string[] args) {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql("Host=127.0.0.1;Port=5432;Database=BeautyBook;Username=postgres;Password=postgres")
                .Options;
                
            using var db = new ApplicationDbContext(options);
            Console.WriteLine("Portfolios count: " + db.Portfolios.Count());
            Console.WriteLine("MUAs count: " + db.MakeupArtistProfiles.Count());
            var mua = db.MakeupArtistProfiles.FirstOrDefault();
            if(mua != null) {
                Console.WriteLine("First MUA Status: " + mua.Status);
                Console.WriteLine("First MUA Quality Score: " + mua.ProfileQualityScore);
            }
        }
    }
}
