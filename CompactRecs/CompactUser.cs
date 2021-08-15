using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TinderAPI.Models;

namespace TinderAutomator.CompactRecs
{
    public class CompactUser
    {
        public string ID { get; set; }
        public TinderAPI.Models.Badge[] Badges { get; set; }

        public DateTime Birthday { get; set; }
        public int Gender { get; set; }
        public string CustomGender { get; set; }
        public string Name { get; set; }
        public string Biography { get; set; }

        public string[] Photos { get; set; }
        public string City { get; set; }
        public string Job { get; set; }
        public string School { get; set; }

        public string[] SexualOrientations { get; set; }

        public CompactUser() { }

        public CompactUser(BaseProfile user)
        {
            ID = user.ID;
            Badges = user.Badges;

            Birthday = user.Birthday;
            Gender = user.Gender;
            CustomGender = user.CustomGender;
            Name = user.Name;
            Biography = user.Biography;

            Photos = user.Photos.Select(
                p => p.URL.IContains("policy") ?
                    "original_" + p.ID + ".jpeg" :
                    System.IO.Path.GetFileName(p.URL)

            ).ToArray();
            City = user.Location != null ? user.Location.City : null;
            if (user.Jobs != null && user.Jobs.Any())
            {
                var job = user.Jobs.First();
                if (job.Title != null)
                {
                    Job = job.Title.Name;
                    if (job.Company != null && !String.IsNullOrWhiteSpace(job.Company.Name))
                        Job += " - " + job.Company.Name;
                }
                else
                    Job = job.Company.Name;
            }
            if (user.Schools != null && user.Schools.Any())
                School = user.Schools.First().Name;

            if (user.SexualOrientations != null && user.SexualOrientations.Any())
                SexualOrientations = user.SexualOrientations.Select(s => s.Name).ToArray();
        }
    }
}
