using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TinderAPI;
using TinderAPI.Models;
using TinderAPI.Models.Recommendations;

namespace TinderAutomator.CompactRecs
{
    [Flags]
    internal enum ProfileInfo
    {
        None = 0,
        Job = 1,
        School = 2,
        Instagram = 4,
        Spotify = 8,
        Gender = 16,
        Bio = 32,
        Photos = 64,
        Social_Media = 128
    }

    public class CompactRec
    {
        public string ContentHash { get; protected set; }
        public long SNumber { get; protected set; }
        public string Type { get; protected set; }
        public int? Distance { get; protected set; }
        public bool IsSuperlikeUpsell { get; protected set; }

        public CompactInsta Instagram { get; protected set; }
        public bool SpotifyConnected { get; protected set; }

        public string[] Vibes { get; protected set; }
        public string[] Interests { get; protected set; }
        public CompactUser User { get; protected set; }
        public bool IsTopPick { get; set; }
        public int TopPickType { get; protected set; }
        public bool HasBeenSuperliked { get; set; }

        public DateTime? LastUpdated { get; set; }

        public CompactRec() { }

        public CompactRec(Recommendation rec)
        {
            ContentHash = rec.ContentHash;
            SNumber = rec.SNumber;
            Type = rec.Type;
            Distance = rec.Distance;
            IsSuperlikeUpsell = rec.IsSuperlikeUpsell;

            if (rec.LiveOps != null)
                Vibes = rec.LiveOps.Vibes.SelectMany(v => v.Prompts.Select(p => p.ResponseID)).ToArray();
            if (rec.Instagram != null)
                Instagram = new CompactInsta(rec.Instagram);
            SpotifyConnected = rec.Spotify != null && rec.Spotify.Connected;

            if (rec.ExperimentInfo != null)
                Interests = rec.ExperimentInfo.UserInterests.SelectedInterests.Select(i => i.Name).ToArray();
            User = new CompactUser(rec.User);
            IsTopPick = rec.IsTopPick;
            TopPickType = rec.TopPickType;
            HasBeenSuperliked = rec.HasBeenSuperliked;
        }

        public bool Update()
        {
            var publicProfile = API.GetPublicProfile(User.ID);
            if (publicProfile != null)
            {
                User.Photos = publicProfile.Photos.Select(
                    p => p.URL.IContains("policy") ?
                        "original_" + p.ID + ".jpeg" :
                        System.IO.Path.GetFileName(p.URL)
                ).ToArray();
                Distance = publicProfile.DistanceMiles;
                if (publicProfile.Location != null)
                    User.City = publicProfile.Location.City;
                User.Biography = publicProfile.Biography;
                User.Badges = publicProfile.Badges;
                SpotifyConnected = publicProfile.SpotifyThemeTrack != null ||
                    publicProfile.SpotifyTopArtists != null;
                if (Instagram == null && publicProfile.InstagramPhotos != null)
                    Instagram = new CompactInsta(publicProfile.InstagramPhotos);
                if (User.Job == null && publicProfile.Jobs.Any())
                {
                    var job = publicProfile.Jobs.First();
                    if (job.Title != null)
                    {
                        User.Job = job.Title.Name;
                        if (job.Company != null && !String.IsNullOrWhiteSpace(job.Company.Name))
                            User.Job += " - " + job.Company.Name;
                    }
                    else
                        User.Job = job.Company.Name;
                }
                if (User.School == null && publicProfile.Schools.Any())
                    User.School = publicProfile.Schools.First().Name;
                return true;
            }
            return false;
        }

        public string GetCustomID()
        {
            ProfileInfo info = ProfileInfo.None;
            if (!String.IsNullOrWhiteSpace(User.Job))
                info |= ProfileInfo.Job;
            if (!String.IsNullOrWhiteSpace(User.School))
                info |= ProfileInfo.School;
            if (Instagram != null)
                info |= ProfileInfo.Instagram;
            if (SpotifyConnected)
                info |= ProfileInfo.Spotify;
            if (User.Gender == 1)
                info |= ProfileInfo.Gender;
            if (!String.IsNullOrWhiteSpace(User.Biography))
                info |= ProfileInfo.Bio;
            if (User.Photos.Length > 5)
                info |= ProfileInfo.Photos;
            if (Recommendation.RGX_SocialMedia.IsMatch(User.Biography))
                info |= ProfileInfo.Social_Media;

            return User.Birthday.Year.ToString() + ":" +
                User.Name + ":" +
                ((int)info).ToString();
        }/// <summary>
         /// Swipes on a user in the recommendations.
         /// </summary>
         /// <param name="interaction">The type of swipe.</param>
         /// <param name="isMatch">When this method returns, contains whether there was a match or not.</param>
         /// <returns>The rate limit, if out of that type of interaction.</returns>
        public DateTime? Swipe(RecInteraction interaction, out bool isMatch)
        {
            switch (interaction)
            {
                case RecInteraction.Like:
                    var likesResponse = API.Like(
                        User.ID,
                        new LikeRequest()
                        {
                            content_hash = ContentHash,
                            liked_content_id = User.Photos.First(),
                            liked_content_type = "photo",
                            s_number = SNumber
                        }
                    );
                    isMatch = likesResponse.IsMatch;
                    if (likesResponse.RateLimitedUntil.HasValue)
                        return Utils.ConvertUnixTimestamp(likesResponse.RateLimitedUntil.Value);
                    else
                        return null;

                case RecInteraction.Pass:
                    API.Pass(User.ID, SNumber);
                    isMatch = false;
                    return null;

                case RecInteraction.Super:
                    var superLikesResponse = API.SuperLike(
                        User.ID,
                        new LikeRequest()
                        {
                            content_hash = ContentHash,
                            liked_content_id = User.Photos.First(),
                            liked_content_type = "photo",
                            s_number = SNumber
                        }
                    );
                    if (superLikesResponse == null)
                    {
                        isMatch = false;
                        return null;
                    }
                    isMatch = superLikesResponse.IsMatch;
                    if (superLikesResponse.LimitExceeded || superLikesResponse.SuperLikes.Remaining == 0)
                        return superLikesResponse.SuperLikes.ResetsAt.ToLocalTime();
                    else
                        return null;

                default:
                    isMatch = false;
                    return null;
            }
        }

        public override string ToString() =>
            String.Format(
                "{0}, {1}, {2} mi",
                User.Name,
                Convert.ToInt32((DateTime.Now - User.Birthday).TotalDays / 365.25),
                Distance ?? -1
            );

    }
}
