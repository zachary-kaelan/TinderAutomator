using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jil;
using TinderAPI.Models.Images;

namespace TinderAutomator.CompactRecs
{
    public class CompactInsta
    {
        public DateTime LastFetch { get; set; }
        public bool CompletedInitialFetch { get; set; }
        public int AccountNumPhotos { get; set; }
        public string[] Photos { get; set; }

        public CompactInsta() { }
        
        public CompactInsta(InstagramPhotoCollection insta)
        {
            LastFetch = insta.LastFetch;
            CompletedInitialFetch = insta.CompletedInitialFetch;
            AccountNumPhotos = insta.AccountNumPhotos;
            if (AccountNumPhotos > 0 && insta.Photos != null)
                Photos = insta.Photos.Select(p => p.Image).ToArray();
        }
    }
}
