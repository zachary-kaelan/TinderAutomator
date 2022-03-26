using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Jil;
using TinderAPI;
using TinderAPI.Models;
using TinderAPI.Models.Images;
using TinderAPI.Models.Recommendations;
using TinderAutomator.CompactRecs;
using TinderAutomator.Properties;

namespace TinderAutomator
{
    public enum RecJudgement
    {
        SuperHardPass = -3,
        HardPass = -2,
        Pass = -1,
        Skipped = 0,
        Like = 1,
        SkipImmunity = 2,
        Superlike = 3,
        FilterImmunity = 4
    }

    class Program
    {

        #region Regex
        private static readonly Regex RGX_MyersBriggs = new Regex(
            @"[IE][NS][FT][JP]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        public static readonly Regex RGX_SocialMedia = new Regex(
            @"(?:\s+|^|, )(?:(?:follow|add) me[^\n]+)?(?:(?:insta|snap|ig|sc|AMOS|AMOI|👻)\w*[:\s@-]*|@)[a-z0-9._-]{7,30}|" +
            @"(?:\s+|^|, )[a-z0-9._-]{7,30} +(?:insta|snap|ig|sc|AMOS|AMOI|👻)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        private static readonly Regex RGX_Kids = new Regex(
            @"\s[1-7] ?(?:yo|yr old|year old)(?:\s|$|\.|,)|" +
            @" a (?:son|daughter|mom|mother|kid|child)|" +
            @"(?<!dog|cat)(?: mom| mother)|" +
            @"single (?:mom|mother)|" +
            @"(?:mom|mother) of (?:one|two|three)(?:[\.,\r\n]|$)|" +
            @"comes? first|" +
            @"(?:have(?: a)?|my) (?:kid|child)|" +
            @"little (?:boy|girl)|" +
            @"if you (?:don'?t|do not) like kid",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        private static readonly Regex RGX_XToMyY = new Regex(
            "looking for the .{3,10} to my .{3,10}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        private static readonly Regex RGX_Height = new Regex(
            @"(?:^|\D)[4-6]'\d{1,2}(?:\D|$|\.)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        private static readonly Regex RGX_Phone = new Regex(
            @"\(?(\d{3})[\) -]*(\d{3})-?(\d{4})",
            RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled
        );

        private static readonly string[] ARR_AstrologySigns = new string[] {
            "Aries",
            "Taurus",
            "Gemini",
            "Cancer",
            "Leo",
            "Virgo",
            "Libra",
            "Scorpio",
            "Sagittarius",
            "Capricorn",
            "Aquarius",
            "Pisces"
        };
        #endregion

        #region Filters
        /// <summary>
        /// Things you don't want to see.
        /// </summary>
        private static readonly SortedDictionary<string, BaseRecFilter> _filters = new SortedDictionary<string, BaseRecFilter>(
            new BaseRecFilter[] {
                new StringRecFilter("strJustAsk", "just ask"), // I have to use a like and get a match to do that
                new ArrayRecFilter(
                    "arrSell",
                    new string[]
                    {
                        " sell",    // Someone looking for a dealer or to deal
                        " a party", // Looking for a party
                        "buy"
                    }
                ),
                new RegexRecFilter(
                    "rgxHobbies",
                    new Regex(
                        // Finally met another person that also enjoys laugher, having a good time, and having fun!
                        @"(?:love|like)s? (?:" +
                        @"to [^\.,\r\n]*(?:laugh|have a good time|have fun)|" +
                        @"[^\.,\r\n]*(?:laughing|having a good time|having fun))",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled
                    )
                ),
                new ArrayRecFilter(
                    "arrSugar",  // Looking for some kind of money-based relationship
                    new string[]
                    {
                        "venmo",
                        "cashapp",
                        "spoil",
                        "$",
                        "paypal",
                        "onlyfans",
                        "sugar daddy",
                        "sugar-daddy",
                        "sugardaddy",
                        "sugar baby",
                        "sugar-baby",
                        "sugarbaby"
                    }
                ),
                new RegexRecFilter(
                    "rgxKids", // I'm not ready to handle someone else's kids
                    RGX_Kids
                ),
                new RegexRecFilter(
                    "rgxSocialMedia", // Plugging their social media
                    RGX_SocialMedia
                ),
                new RegexRecFilter(
                    "rgxXToMyY", // Lazy bio
                    new Regex(
                        "looking for the .{3,10} to my .{3,10}",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled
                    )
                ),
                new ArrayRecFilter(
                    "arrPersonalityTraits",
                    new string[]
                    {
                        // https://tvtropes.org/pmwiki/pmwiki.php/Main/InformedAttribute
                        // Everyone says they are these things or want these things
                        "laid back",
                        "down to earth",
                        "sweet",
                        "funny",
                        "nice",
                        "loyal",
                        "caring",
                        "open minded",
                        "honest",
                        "friendly",
                        "bored"
                    }
                ),
                new ArrayRecFilter(
                    "arrAbsent",
                    new string[]
                    {
                        // They don't use the app often
                        "not on here",
                        "here much",
                        "don't get on here",
                        "don't be on here",
                        "don't check this",
                        "bad at responding",
                        "for the summer",
                        "for this summer",
                        "moving away"
                    }
                ),
                new ArrayRecFilter(
                    "arrPoly",
                    new string[]
                    {
                        "poly",
                        "non-mono",
                        "non mono",
                        "nonmono",
                        "open relationship",
                        "a third",
                        "join us",
                        "me and my husband",
                        "me and my boyfriend",
                        "my husband and I",
                        "my boyfriend and I"
                    }
                )
            }.ToDictionary(f => f.Name, f => f)
        );

        /// <summary>
        /// Filters that give positive score.
        /// </summary>
        private static readonly KeyValuePair<BaseRecFilter, int>[] _positiveFilters = new KeyValuePair<BaseRecFilter, int>[]
        {
            new KeyValuePair<BaseRecFilter, int>(
                new ArrayRecFilter(
                    "arrAutism",
                    new string[]
                    {
                        // Obviously, because I am on the spectrum
                        "on the spectrum",
                        "am autistic",
                        "have autism",
                        "suffer from autism",
                        "ASD",
                        "autism spectrum disorder"
                    }
                ), 8
            ),
            new KeyValuePair<BaseRecFilter, int>(
                new ArrayRecFilter(
                    "arrReferences",
                    new string[]
                    {
                        // Quotes from shows I like
                    }
                ), 3
            ),
            new KeyValuePair<BaseRecFilter, int>(
                new ArrayRecFilter(
                    "arrCats",
                    new string[]
                    {
                        " cat ",
                        " cats ",
                        "kitten"
                    }
                ), 3
            ),
            new KeyValuePair<BaseRecFilter, int>(
                new ArrayRecFilter(
                    "arrTypingStyle",
                    new string[]
                    {
                        // They put that extra bit of effort into their bio
                        "TLDR",
                        "TL;DR",
                        "Pros",
                        "Cons",
                        "\n* ",
                        "\n *",
                        "\n1.",
                        "\n 1.",
                        "\n-",
                        "\n -",
                        "\n•",
                        "\n •",
                        ":\n",
                        "edit:",
                        "edit-",
                        "update:",
                        "update-"
                    }
                ), 4
            ),
            new KeyValuePair<BaseRecFilter, int>(
                new ArrayRecFilter(
                    "arrHobbies",
                    new string[]
                    {
                        // Shared hobbies
                        "video game",
                        "computer science",
                        "software"
                    }
                ), 3
            )
        };

        /// <summary>
        /// If a bio filter can mean the same thing as an interest.
        /// </summary>
        private static readonly SortedDictionary<string, string> _filterOverlap = new SortedDictionary<string, string>()
        {
            { "Cat Lover", "arrCats" },
        };

        /// <summary>
        /// Interests that are ignored if the person is an upsell.
        /// </summary>
        private static readonly SortedSet<string> _upsellImmuneInterests = new SortedSet<string>()
        {

        };

        /// <summary>
        /// Score modifiers for every "interest" listed on the recommendation's profile.
        /// </summary>
        private static readonly SortedDictionary<string, InterestScore> _interestScores = new SortedDictionary<string, InterestScore>()
        {
            // Some example modifiers
            // My personal set had 60 modifiers
            { "Mental Health Awareness",                      +5 },
            { "Feminism",                                     +4 },
            { "Activism",                                     +2 },
            { "LGBTQ+ Rights",              new InterestScore(+4, "LGBTQ") },
            { "LGBTQ+",                     new InterestScore(+3, "LGBTQ") },
            { "Black Lives Matter",                           +5 },
            { "Outdoors",                   new InterestScore(-9, "Sun") },
            { "Walking",                    new InterestScore(-8, "Sun") },
            { "Cat Lover",                                    +9 }
        };

        /// <summary>
        /// Recommendations that are to be manually ignored in any data review.
        /// </summary>
        private static SortedSet<string> _ignore = new SortedSet<string>()
        {
        };
        #endregion

        #region Setup
        private const string PATH_MAIN = "";
        private const string PATH_HISTORY = PATH_MAIN + @"History\";
        private const string PATH_LOGS = PATH_MAIN + @"Logs\";
        private const string PATH_USERS = PATH_MAIN + @"Users\";
        private const string PATH_TOP_PICKS = PATH_MAIN + @"Top Picks\";
        private const string PATH_QUERIES = PATH_MAIN + @"Queries\";
        private const string PATH_REVIEW = PATH_MAIN + @"Superlikes Review\";

        private static DateTime _today = DateTime.Now;
        private static readonly double _meYearsOld = (new DateTime(1999, 8, 5) - _today).TotalDays / 365;
        private static int _numPassed = 0;
        private static int _numLiked = 0;
        private static int _numSkipped = 0;

        private static StreamWriter _log;
        private static StreamWriter _passed;
        private static StreamWriter _liked;
        private static StreamWriter _superliked;
        private static StreamWriter _duplicates;
        private static StreamWriter _skipped;
        private static StreamWriter _history;
        private static StreamWriter _topPicks;
        private static StreamWriter _superlikeWorthy;
        private static StreamWriter _scores;
        private static StreamWriter _upsell;
        private static StreamWriter _deleted;

        private static SortedSet<string> UserHistory;
        private static SortedSet<string> TopPicks;
        private static SortedSet<string> Skipped;
        private static SortedSet<string> SuperlikeWorthy;

        private static DateTime? LikesRefresh = null;
        private static DateTime? SuperlikeRefresh = null;
        private static DateTime? TopPicksRefresh = null;

        static Program()
        {
            if (!Directory.Exists(PATH_HISTORY))
                Directory.CreateDirectory(PATH_HISTORY);
            if (!Directory.Exists(PATH_LOGS))
                Directory.CreateDirectory(PATH_LOGS);
            if (!Directory.Exists(PATH_USERS))
                Directory.CreateDirectory(PATH_USERS);
            

            if (File.Exists(PATH_HISTORY + "Passed.txt"))
                _numPassed = File.ReadAllLines(PATH_HISTORY + "Passed.txt").Length;
            if (File.Exists(PATH_HISTORY + "Liked.txt"))
                _numLiked = File.ReadAllLines(PATH_HISTORY + "Liked.txt").Length;

            File.WriteAllLines(
                PATH_HISTORY + "Duplicates.txt",
                File.ReadAllLines(PATH_HISTORY + "Duplicates.txt").Distinct()
            );

            _log = new StreamWriter(PATH_LOGS + _today.ToString("yyyy'-'MM'-'dd") + ".txt", true) { AutoFlush = true };
            _passed = new StreamWriter(PATH_HISTORY + "Passed.txt", true) { AutoFlush = true };
            _liked = new StreamWriter(PATH_HISTORY + "Liked.txt", true) { AutoFlush = true };
            _duplicates = new StreamWriter(PATH_HISTORY + "Duplicates.txt", true) { AutoFlush = true };
        }

        static void Main(string[] args)
        {
            Console.WindowWidth = 120;
            Console.WindowHeight = 72;

            TopPicks = File.Exists(PATH_HISTORY + "TopPicks.txt") ?
                new SortedSet<string>(File.ReadLines(PATH_HISTORY + "TopPicks.txt")) :
                new SortedSet<string>();
            _topPicks = new StreamWriter(PATH_HISTORY + "TopPicks.txt", true) { AutoFlush = true };

            UserHistory = File.Exists(PATH_HISTORY + "UserHistory.txt") ?
                new SortedSet<string>(File.ReadLines(PATH_HISTORY + "UserHistory.txt")) :
                new SortedSet<string>();
            UserHistory.UnionWith(_ignore);
            _history = new StreamWriter(PATH_HISTORY + "UserHistory.txt", true) { AutoFlush = true };

            Skipped = File.Exists(PATH_HISTORY + "Skipped.txt") ?
                new SortedSet<string>(File.ReadAllLines(PATH_HISTORY + "Skipped.txt")) : 
                new SortedSet<string>();
            Skipped.ExceptWith(UserHistory);
            _numSkipped = Skipped.Count;
            _skipped = new StreamWriter(PATH_HISTORY + "Skipped.txt", true) { AutoFlush = true };

            SortedSet<string> superliked = new SortedSet<string>(File.ReadAllLines(PATH_HISTORY + "Superliked.txt"));
            _superliked = new StreamWriter(PATH_HISTORY + "Superliked.txt", true) { AutoFlush = true };

            SuperlikeWorthy = File.Exists(PATH_HISTORY + "SuperlikeWorthy.txt") ?
                new SortedSet<string>(File.ReadAllLines(PATH_HISTORY + "SuperlikeWorthy.txt")) :
                new SortedSet<string>();
            _superlikeWorthy = new StreamWriter(PATH_HISTORY + "SuperlikeWorthy.txt", true) { AutoFlush = true };

            SuperlikeWorthy.ExceptWith(superliked);
            SuperlikeWorthy.ExceptWith(_ignore);
            SuperlikeWorthy.ExceptWith(Skipped);

            _scores = new StreamWriter(PATH_HISTORY + "Scores.txt", true) { AutoFlush = true };

            SetCooldowns();

            // Using our superlike if we have one and a candidate
            if (SuperlikeWorthy.Count > 0 && (SuperlikeRefresh == null || DateTime.Now > SuperlikeRefresh.Value))
            {
                var rankedSuperlikeWorthy = SuperlikeWorthy.Select(
                    s =>
                    {
                        var rec = RecheckRec(s, false, out int score);
                        return new KeyValuePair<int, string>(score, s);
                    }
                ).OrderByDescending(s => s.Key);

                // 
                foreach (var superlikeWorthy in rankedSuperlikeWorthy)
                {
                    if (superlikeWorthy.Key > 25)
                    {
                        if (UseSuperlike(superlikeWorthy.Value))
                            break;
                    }
                }
            }

            Console.WriteLine(
                "{0} liked, {1} passed, {2} total, {3} superlike worthy, {4} skipped", 
                _numLiked, _numPassed, UserHistory.Count, SuperlikeWorthy.Count, _numSkipped);

            SetCooldowns();

            Array.Clear(_curRecLog, 0, 4);
            CheckTopPicks();

            if (LikesRefresh != null)
            {
                Log("IDLE - Out of likes, waiting until {0}", LikesRefresh.Value);
                SpinWait.SpinUntil(() => DateTime.Now > LikesRefresh.Value);
            }

            while (true)
            {
                var numSwipes = RunLoop();
                Log("IDLE - Out of likes after {0} swipes, waiting until {1}", numSwipes, LikesRefresh.Value);
                if (DateTime.Now.Day > _today.Day)
                {
                    // New day, new log
                    _today = DateTime.Now;
                    _log.Close();
                    _log = new StreamWriter(PATH_LOGS + _today.ToString("yyyy'-'MM'-'dd") + ".txt", true) { AutoFlush = true };
                }
                SpinWait.SpinUntil(() => DateTime.Now > LikesRefresh.Value);
                LikesRefresh = null;
                API.GetNewAuthToken();
            }
        }


        /// <summary>
        /// Checks and stores how long the program has to wait to take certain actions.
        /// </summary>
        private static void SetCooldowns()
        {
            var cooldowns = API.GetCooldowns();
            if (cooldowns == null)
            {
                API.GetNewAuthToken();
                cooldowns = API.GetCooldowns();
            }
            if (cooldowns.Likes.RateLimitedUntil.HasValue)
                LikesRefresh = Utils.ConvertUnixTimestamp(cooldowns.Likes.RateLimitedUntil.Value);
            if (cooldowns.SuperLikes.Remaining == 0)
                SuperlikeRefresh = cooldowns.SuperLikes.ResetsAt.ToLocalTime();
        }
        #endregion

        #region Logging
        private static string[] _curRecLog = new string[4];

        private static void Log(string format, params object[] args) =>
            Log(String.Format(format, args));

        private static void Log(string line)
        {
            _log.WriteLine(line);
            Console.WriteLine(line);
        }

        private static void WriteCurRecLog()
        {
            foreach (var line in _curRecLog)
            {
                if (!String.IsNullOrWhiteSpace(line))
                    Log(line);
            }
            Array.Clear(_curRecLog, 0, 4);
        }
        #endregion

        /// <summary>
        /// What does the actual searching and swiping.
        /// </summary>
        /// <returns>How many recommendations it looked through before running out of likes.</returns>
        private static int RunLoop()
        {
            Array.Clear(_curRecLog, 0, 4);
            int numSwipes = 0;
            var skippedToLike = Skipped.Except(SuperlikeWorthy).ToArray();
            // Just automatically liking anyone that was skipped last time
            foreach (var skippedRecId in skippedToLike)
            {
                var rec = Utils.LoadJSON<CompactRec>(PATH_USERS + skippedRecId + ".json", API._opts);
                LikesRefresh = rec.Swipe(RecInteraction.Like, out bool isMatch);
                ++_numLiked;
                ++numSwipes;
                _liked.WriteLine(rec.User.ID);

                UserHistory.Add(rec.User.ID);
                _history.WriteLine(rec.User.ID);

                Skipped.Remove(skippedRecId);
                --_numSkipped;
                
                Judge(
                    rec,
                    TopPicks.Contains(rec.GetCustomID()),
                    out int skippedScore
                );

                _curRecLog[0] = String.Format(
                    "LIKE - {0} with score {1} brought back after being skipped",
                    skippedRecId,
                    skippedScore
                );

                if (isMatch)
                    _curRecLog[4] = "MATCH - IT'S A MATCH!";

                WriteCurRecLog();

                Thread.Sleep(rec.GetJudgementTime());

                if (LikesRefresh != null)
                    return numSwipes;
            }


            var gen = new Random();

            foreach (var recTemp in API.GetRecommendations())
            {
                if (LikesRefresh != null)
                    return numSwipes;

                if (
                    Skipped.Contains(recTemp.User.ID) || 
                    SuperlikeWorthy.Contains(recTemp.User.ID)
                ) {
                    // Recommendations being saved for later that showed up again
                    Log("SKIP - {0} because of duplicate", recTemp.User.ID);
                    _duplicates.WriteLine(recTemp.User.ID);
                    continue;
                }
                ++numSwipes;
                var rec = new CompactRec(recTemp);
                rec.SaveAs(PATH_USERS + rec.User.ID + ".json", API._opts);

                string topPickString = rec.GetCustomID();
                double rightSwipeRatio = (double)_numLiked / (_numLiked + _numPassed);
                bool isTopPick = TopPicks.Contains(topPickString);
                RecJudgement judgement = Judge(rec, isTopPick, out int score);
                _scores.WriteLine("{0}: {1} ({2})", rec.User.ID, score, judgement);
                bool isMatch = false;
                var now = DateTime.Now;

                if (isTopPick || rec.IsSuperlikeUpsell)
                    _upsell.WriteLine(rec.User.ID);

                Thread.Sleep(rec.GetJudgementTime());

                // Being too picky or not picky enough unfortunately penalizes how often others see you in their recommendations
                if (rightSwipeRatio < 0.25)
                {
                    if (judgement == RecJudgement.Pass)
                    {
                        if (_numSkipped > 0)
                        {
                            var firstSkipped = Utils.LoadJSON<CompactRec>(
                                PATH_USERS + Skipped.Except(SuperlikeWorthy).First() + ".json",
                                API._opts
                            );
                            firstSkipped.Swipe(RecInteraction.Like, out isMatch);
                            Skipped.Remove(firstSkipped.User.ID);
                            --_numSkipped;
                            UserHistory.Add(firstSkipped.User.ID);
                            _history.WriteLine(rec.User.ID);

                            Judge(
                                firstSkipped, 
                                TopPicks.Contains(rec.GetCustomID()), 
                                out int skippedScore
                            );
                            _curRecLog[0] = String.Format(
                                "LIKE - {0} with score {1} brought back because of low swipe ratio",
                                firstSkipped.User.ID,
                                skippedScore
                            );
                        }
                        else if (score >= -5 || (rightSwipeRatio < 0.15 && score >= -10))
                        {
                            judgement = RecJudgement.Like;
                            _curRecLog[0] = String.Format(
                                "LIKE - {0} with score {1} because of low swipe ratio",
                                rec.User.ID,
                                score
                            );
                        }
                    }
                }
                else if (judgement == RecJudgement.Like)
                {
                    if (rightSwipeRatio > 0.65)
                    {
                        // This code has never been executed
                        _curRecLog[0] = String.Format(
                            "SKIP - {0} with score {1} because of excessive swipe ratio", 
                            rec.User.ID, 
                            score
                        );
                        judgement = RecJudgement.Skipped;
                    }
                    else
                        _curRecLog[0] = String.Format(
                            "LIKE - {0} with score {1}", 
                            rec.User.ID, 
                            score
                        );
                }

                if (judgement == RecJudgement.Skipped)
                {
                    _skipped.WriteLine(rec.User.ID);
                    Skipped.Add(rec.User.ID);
                }
                else
                {
                    switch(judgement)
                    {
                        case RecJudgement.Superlike:
                        case RecJudgement.FilterImmunity:
                            SuperlikeWorthy.Add(rec.User.ID);
                            _superlikeWorthy.WriteLine(rec.User.ID);
                            
                            if (UseSuperlike() != rec.User.ID)
                                _curRecLog[0] = String.Format(
                                    "SKIP - {0} with score {1}; out of superlikes", 
                                    rec.User.ID, 
                                    score
                                );
                            break;

                        case RecJudgement.Like:
                        case RecJudgement.SkipImmunity:
                            LikesRefresh = rec.Swipe(RecInteraction.Like, out isMatch);
                            ++_numLiked;
                            _liked.WriteLine(rec.User.ID);

                            UserHistory.Add(rec.User.ID);
                            _history.WriteLine(rec.User.ID);
                            break;

                        case RecJudgement.Pass:
                        case RecJudgement.HardPass:
                        case RecJudgement.SuperHardPass:
                            _curRecLog[0] = String.Format(
                                "PASS - {0} with score {1}", 
                                rec.User.ID, 
                                score
                            );

                            rec.Swipe(RecInteraction.Pass, out isMatch);
                            ++_numPassed;
                            _passed.WriteLine(rec.User.ID);

                            UserHistory.Add(rec.User.ID);
                            _history.WriteLine(rec.User.ID);
                            break;
                    }
                }

                if (isMatch)
                    _curRecLog[4] = "MATCH - IT'S A MATCH!";
                WriteCurRecLog();

                if (numSwipes % 15 == 0)
                    Log("RATIO - Right swipe ratio is {0}", rightSwipeRatio);

                CheckTopPicks();

                UseSuperlike();
            }

            return numSwipes;
        }

        /// <summary>
        /// Looks at the current top picks, if available, and likes one.
        /// </summary>
        private static void CheckTopPicks()
        {
            if (!Directory.Exists(PATH_TOP_PICKS))
            {
                Directory.CreateDirectory(PATH_TOP_PICKS);
            }

            if (
                (TopPicksRefresh == null || 
                    DateTime.Now > TopPicksRefresh.Value) && 
                (LikesRefresh == null || 
                    DateTime.Now > LikesRefresh.Value))
            {
                Array.Clear(_curRecLog, 0, 4);

                var topPicksResponse = API.GetTopPicks();
                if (topPicksResponse.FreeLikesRemaining == 0 || topPicksResponse.Results.Length == 9)
                {
                    TopPicksRefresh = Utils.ConvertUnixTimestamp(topPicksResponse.TopPicksRefreshTime);
                    return;
                }

                SortedDictionary<long, string[]> recLogs = new SortedDictionary<long, string[]>();
                List<KeyValuePair<CompactRec, int>> recScores = new List<KeyValuePair<CompactRec, int>>();
                foreach (var topPick in topPicksResponse.Results)
                {
                    var rec = new CompactRec(topPick);
                    rec.SaveAs(PATH_TOP_PICKS + topPick.User.ID + ".json", API._opts);
                    var thisTopPickString = rec.GetCustomID();
                    if (!TopPicks.Contains(thisTopPickString))
                    {
                        TopPicks.Add(thisTopPickString);
                        _topPicks.WriteLine(thisTopPickString);
                    }

                    var topPickJudgement = Judge(rec, true, out int topPickScore);
                    recScores.Add(new KeyValuePair<CompactRec, int>(rec, topPickScore));
                    _curRecLog[0] = String.Format(
                        "{0} with score {1}",
                        rec.SNumber,
                        topPickScore
                    );
                    var curTopPickRecLog = new string[4];
                    Array.Copy(_curRecLog, curTopPickRecLog, 4);
                    recLogs.Add(rec.SNumber, curTopPickRecLog);
                    Array.Clear(_curRecLog, 0, 4);
                }

                Log("");
                Log("TOP PICKS");
                Log("~~~~~~~~~");
                recScores = recScores.OrderByDescending(s => s.Value).ToList();
                var bestTopPickScore = recScores[0];
                foreach (var score in recScores)
                {
                    Array.Copy(recLogs[score.Key.SNumber], _curRecLog, 4);
                    WriteCurRecLog();
                }
                Console.WriteLine();

                Thread.Sleep(5000);
                bool isTopPickMatch = false;
                if (bestTopPickScore.Value > 20 && SuperlikeRefresh == null)
                {
                    // Time to superlike
                    isTopPickMatch = API.RateTopPick(
                        bestTopPickScore.Key.SNumber,
                        bestTopPickScore.Key.User.ID,
                        true,
                        out PublicProfile profile
                    );
                    _superliked.WriteLine(profile.ID);
                    SetCooldowns();
                    Log(
                        "TOPPICK - Superliked {0} with score {1}", 
                        profile.ID, 
                        bestTopPickScore
                    );
                }
                else
                {
                    isTopPickMatch = API.RateTopPick(
                        bestTopPickScore.Key.SNumber,
                        bestTopPickScore.Key.User.ID,
                        false,
                        out PublicProfile profile
                    );
                    Log(
                        "TOPPICK - Liked {0} with score {1}",
                        profile.ID,
                        bestTopPickScore
                    );
                }

                if (isTopPickMatch)
                    Log("\tMATCH - IT'S A MATCH!");

                TopPicksRefresh = Utils.ConvertUnixTimestamp(topPicksResponse.TopPicksRefreshTime);
                UserHistory.Add(bestTopPickScore.Key.User.ID);
                _history.WriteLine(bestTopPickScore.Key.User.ID);
            }
        }

        #region Superlikes
        /// <summary>
        /// Uses a superlike on the top ranked recommendation.
        /// </summary>
        /// <returns>The ID of the superliked recommendation, or null if it superliked nobody.</returns>
        private static string UseSuperlike()
        {
            if (SuperlikeWorthy.Count > 0 && (SuperlikeRefresh == null || DateTime.Now > SuperlikeRefresh.Value))
            {
                var topSuperlikeRec = RankSuperlikeWorthy(false);

                if (topSuperlikeRec.Value != 0)
                {
                    SuperlikeRefresh = topSuperlikeRec.Key.Swipe(RecInteraction.Super, out bool isMatch);
                    var id = topSuperlikeRec.Key.User.ID;
                    SuperlikeWorthy.Remove(id);
                    if (UserHistory.Add(id))
                        _history.WriteLine(id);

                    _superliked.WriteLine(id);
                    Thread.Sleep(3000);
                    if (API.GetMyLikes().First().User.ID != id)
                        UseSuperlike();
                    if (SuperlikeRefresh.Value < DateTime.Now.AddHours(1))
                        SuperlikeRefresh = SuperlikeRefresh.Value.AddDays(1);

                    Log(
                        "SUPERLIKE - {0} with score {1}",
                        id,
                        topSuperlikeRec.Value
                    );

                    if (isMatch)
                        Log("MATCH - IT'S A MATCH!");

                    return id;
                }
            }

            return null;
        }

        /// <summary>
        /// Uses a superlike on the specified recommendation.
        /// </summary>
        /// <param name="superlikeRecID">The ID of the recommendation to be superliked.</param>
        /// <returns>Whether the superlike succeeded.</returns>
        private static bool UseSuperlike(string superlikeRecID)
        {
            if (SuperlikeRefresh == null || DateTime.Now > SuperlikeRefresh.Value)
            {
                var superlikeRec = Utils.LoadJSON<CompactRec>(PATH_USERS + superlikeRecID + ".json", API._opts);
                var thisTopPickString = superlikeRec.GetCustomID();
                var superlikeRecJudgement = Judge(
                    superlikeRec,
                    TopPicks.Contains(thisTopPickString),
                    out int superlikeRecScore
                );

                SuperlikeRefresh = superlikeRec.Swipe(RecInteraction.Super, out bool isMatch);
                SuperlikeWorthy.Remove(superlikeRec.User.ID);
                if (UserHistory.Add(superlikeRec.User.ID))
                    _history.WriteLine(superlikeRec.User.ID);

                _superliked.WriteLine(superlikeRecID);
                Thread.Sleep(3000);
                var myLikes = API.GetMyLikes();
                if (!myLikes.Any() || myLikes.First().User.ID != superlikeRecID)
                    return false;
                if (!SuperlikeRefresh.HasValue)
                {
                    SetCooldowns();
                    return false;
                }
                if (SuperlikeRefresh.Value < DateTime.Now.AddHours(1))
                    SuperlikeRefresh = SuperlikeRefresh.Value.AddDays(1);

                Log(
                    "SUPERLIKE - {0} with score {1}",
                    superlikeRec.User.ID,
                    superlikeRec
                );

                if (isMatch)
                    Log("MATCH - IT'S A MATCH!!! :O :O :O");

                return true;
            }

            return false;
        }

        /// <summary>
        /// Ranks all the currently listed superlike worthy, then returns the top one.
        /// </summary>
        /// <param name="review">Whether this is being used to review who is scored highest.</param>
        /// <returns></returns>
        private static KeyValuePair<CompactRec, int> RankSuperlikeWorthy(bool review = true)
        {
            Array.Clear(_curRecLog, 0, 4);
            SortedDictionary<long, string[]> recLogs = new SortedDictionary<long, string[]>();
            List<KeyValuePair<CompactRec, int>> recScores = new List<KeyValuePair<CompactRec, int>>();
            var now = DateTime.Now;
            foreach (var superlikeRecID in SuperlikeWorthy)
            {
                var superlikeRec = Utils.LoadJSON<CompactRec>(PATH_USERS + superlikeRecID + ".json", API._opts);
                if (
                    review && (
                        !superlikeRec.LastUpdated.HasValue ||
                        (now - superlikeRec.LastUpdated.Value).TotalDays > 1
                    ) && superlikeRec.Update()
                )
                {
                    superlikeRec.LastUpdated = now;
                    superlikeRec.SaveAs(PATH_USERS + superlikeRecID + ".json", API._opts);
                }

                var thisTopPickString = superlikeRec.GetCustomID();
                var superlikeRecJudgement = Judge(
                    superlikeRec,
                    TopPicks.Contains(thisTopPickString),
                    out int superlikeRecScore
                );

                if (TopPicks.Contains(thisTopPickString))
                    superlikeRec.IsTopPick = true;

                if (review)
                {
                    _curRecLog[0] = String.Format(
                        "{0} with score {1}",
                        superlikeRecID,
                        superlikeRecScore
                    );
                    var curSuperlikeRecLog = new string[4];
                    Array.Copy(_curRecLog, curSuperlikeRecLog, 4);
                    recLogs.Add(superlikeRec.SNumber, curSuperlikeRecLog);
                    Array.Clear(_curRecLog, 0, 4);
                }
                

                recScores.Add(new KeyValuePair<CompactRec, int>(superlikeRec, superlikeRecScore));
            }

            if (recScores.Any())
            {
                if (review)
                {

                    if (!Directory.Exists(PATH_REVIEW))
                    {
                        Directory.CreateDirectory(PATH_REVIEW);
                    }

                    FileStream detailsFile = new FileStream(
                        PATH_REVIEW + DateTime.Now.ToString("MM''dd'-'hh") + ".txt",
                        FileMode.Create
                    );
                    StreamWriter detailsWriter = new StreamWriter(detailsFile);
                    recScores = recScores.OrderByDescending(s => s.Value).ToList();
                    Log("");
                    Log("SUPERLIKE");
                    Log("~~~~~~~~~");

                    foreach (var score in recScores)
                    {
                        Array.Copy(recLogs[score.Key.SNumber], _curRecLog, 4);
                        var rec = score.Key;
                        WriteReadableInfoTo(rec, detailsWriter, true);
                        WriteCurRecLog();
                    }

                    detailsWriter.Close();
                    detailsFile.Close();

                    Console.WriteLine();
                }
                return recScores[0];
            }
            else
                return new KeyValuePair<CompactRec, int>();
        }
        #endregion

        #region Judging
        private static readonly double _bioLengthLogCap = Math.Log10(500);

        /// <summary>
        /// The primary algorithm that determines how good a recommendation is.
        /// May be subjective.
        /// </summary>
        /// <param name="rec">The recommendation to be judged.</param>
        /// <param name="isTopPick">Whether this recommend is a top pick.</param>
        /// <param name="score">The numerical score given for the recommendation.</param>
        /// <returns>The final judgement of the recommendation, based on score categories.</returns>
        private static RecJudgement Judge(CompactRec rec, bool isTopPick, out int score)
        {
            // A few technical things about Tinder:
            //   Every free user gets 100 likes every 12 hours and 1 superlike every 24 hours.
            //   Swiping right too often or too little penalizes the user.
            //   In the app, Tinder will sometimes advertise buying a superlike for popular recommendations.
            //   In the API, 10% of recommendations have an "is_superlike_upsell" flag.      
            //
            // Goals:
            //   At least 25% of recommendations must be like worthy.
            //   At least 35% of recommendations must not be like worthy.
            //   Around 0.5% of likes must be superlike worthy.
            // 
            // Main Problem: Getting a large enough right-swipe ratio
            //   48.17% of recommendations have no interests.
            //   26.74% of recommendations have an empty bio.
            //   29.73% of recommendations have a bio length less than a sentence.
            //   5.34% of recommendations had no immediate problems, according to my queries.
            // 
            // Maintaining >25%:
            //   Every recommendation is given a starting score of 5, the minimum for a like.
            //   10% of recommendations get a big boost from Tinder's popularity flag.
            //   Being nearby gives up to 5 extra.
            //   Any recommendation with added connections or info gets extra for it.
            //
            // None of that is enough, so scores close enough to a Like are considered if the ratio is too low.
            // 
            // Tweaking the Algorithm:
            //   Using RankSuperlikeWorthy and personally reviewing the recommendations.
            //   Using ListJudgementRates and adjusting the judgement category cutoffs.
            //   Getting terrible matches and using RecheckRec to see why those recommendations were chosen.

            double scoreDbl = 5;
            double bioScore = 0;
            double locCloseScore = 0;
            double lengthModifier = 0;
            double totalInterestValue = 0;

            string bio = rec.User.Biography;
            bool emptyBio = String.IsNullOrWhiteSpace(bio);
            if (!emptyBio)
                bio = bio.Replace('‘', '\''); // weird symbol
            string modifiedBio = bio;

            bool isUpsell = isTopPick || rec.IsSuperlikeUpsell;

            if (!isTopPick && emptyBio && rec.Interests == null)
            {
                // Unacceptable
                _curRecLog[1] = "\tFILTER - No info";
                if (isUpsell && rec.Distance.HasValue && rec.Distance.Value <= 50)
                {
                    score = 3;
                    _curRecLog[2] = "\tJUDGE - " + (isTopPick ? "pflTopPick" : "pflSuperUpsell");
                    return RecJudgement.Pass;
                }
                else
                {
                    score = -50;
                    return RecJudgement.SuperHardPass;
                }
            }

            SortedSet<string> positiveFilters = new SortedSet<string>();
            SortedSet<string> negativeFilters = new SortedSet<string>();

            if (!emptyBio)
            {
                if (RGX_Phone.IsMatch(bio) && !bio.IContains("555"))
                {
                    _curRecLog[1] = "\tFILTER - Scammer";
                    score = -50;
                    return RecJudgement.SuperHardPass;
                }
            }

            #region Location
            if (!String.IsNullOrWhiteSpace(rec.User.City))
            {
                // Distance is somewhat random, city isn't
                if (rec.User.City == "Apex")
                    locCloseScore += 5;
                else if (rec.User.City == "Holly Springs")
                    locCloseScore += 3;
                else if (
                    rec.User.City == "Raleigh" ||
                    rec.User.City == "Durham"
                )
                    locCloseScore += 2;
            }

            if (rec.Distance.HasValue)
            {
                if (rec.Distance < 10)
                    locCloseScore += 13 - rec.Distance.Value;
                else if (rec.Distance < 25)
                    locCloseScore += (25 - rec.Distance.Value) / 5.0;
                else if (rec.Distance > 50)
                {
                    negativeFilters.Add("locFar");
                    locCloseScore = 0;
                }
            }

            if (!negativeFilters.Contains("locFar"))
            {
                // Sometimes your recommendations don't even live nearby
                if (Regex.IsMatch(bio, "for (?:the |this )(?:spring|summer|fall|winter)"))
                {
                    negativeFilters.Add("locFar");
                    locCloseScore = 0;
                }
            }

            if (locCloseScore > 0 && !negativeFilters.Contains("locFar"))
            {
                // Arbitrary calculation of how important distance is
                locCloseScore = Math.Sqrt(locCloseScore);
                if (locCloseScore >= 2.5)
                    positiveFilters.Add("locClose");
                locCloseScore = Math.Min(3, locCloseScore);
                scoreDbl += locCloseScore;
            }
            #endregion

            if (isUpsell)
            {
                if (isTopPick)
                {
                    scoreDbl += 5;
                    positiveFilters.Add("pflTopPick");
                }
                else if (rec.IsSuperlikeUpsell)
                {
                    scoreDbl += 4;
                    positiveFilters.Add("pflSuperUpsell");
                }

                // If they're far then being attractive is less relevant
                if (negativeFilters.Contains("locFar"))
                    scoreDbl -= 2;
            }

            #region Miscellaneous Extras
            // Extra effort
            if (
                (
                    rec.User.Badges != null &&
                    rec.User.Badges.Any(
                        b => b.Type == "selfie_verified"
                    )
                ) && !isUpsell
            )
                scoreDbl += 1;
            if (rec.Instagram != null)
                scoreDbl += isUpsell ? 1 : 2;
            if (rec.SpotifyConnected)
                scoreDbl += 1;


            var extras = 0;
            bio = bio.Replace("'", "");

            // If they have a job
            if (!String.IsNullOrWhiteSpace(rec.User.Job) ||
                bio.IContains("working as") ||
                bio.IContains(" gig ") ||
                bio.IContains("work with") ||
                bio.IContains("working with"))
            {
                ++extras;
            }

            // If they have education
            if (!String.IsNullOrWhiteSpace(rec.User.School) ||
                bio.IContains("studying") ||
                bio.IContains("degree") ||
                bio.IContains("associates") ||
                bio.IContains("bachelors") ||
                bio.IContains("masters") ||
                bio.IContains("doctorate") ||
                bio.IContains("major") ||
                Regex.IsMatch(bio, @" (?:['‘]2\d|202\d)(?:\s|$)"))
            {
                ++extras;
            }

            if (extras > 0)
            {
                // Small boost
                scoreDbl += extras * 1.5;

                if (extras == 2)
                    positiveFilters.Add("pflGoals");
            }
            #endregion

            #region Biography
            int positiveFiltersScore = 0;
            if (emptyBio)
            {
                // No effort into bio
                scoreDbl -= 7; 
                negativeFilters.Add("pflEmptyBio");
            }
            else
            {
                foreach (var filter in _positiveFilters)
                {
                    if (filter.Key.Match(bio))
                    {
                        positiveFilters.Add(filter.Key.Name);
                        positiveFiltersScore += filter.Value;
                    }
                }

                scoreDbl += positiveFiltersScore;

                // Remove "non-bio" information
                modifiedBio = RGX_SocialMedia.Replace(modifiedBio, "");
                modifiedBio = RGX_MyersBriggs.Replace(modifiedBio, "");
                modifiedBio = RGX_Height.Replace(modifiedBio, "");
                foreach(var sign in ARR_AstrologySigns)
                {
                    modifiedBio = modifiedBio.Replace(sign, "");
                }

                // Remove excess before counting
                modifiedBio = Regex.Replace(modifiedBio, @"\?{2,}", "?");
                modifiedBio = Regex.Replace(modifiedBio, @"\!{2,}", "!");
                modifiedBio = Regex.Replace(modifiedBio, @"\n{2,}", "\n").Trim();

                // Asking questions
                bioScore += modifiedBio.ICount("?") * 2;

                // Extra lines, extra info, extra formatting
                bioScore += Math.Sqrt(modifiedBio.ICount("\n"));

                if (bioScore >= 4)
                    positiveFilters.Add("pflFormatting"); 

                // Condense down
                modifiedBio = Regex.Replace(modifiedBio, "[^A-Za-z',!?()\\\\/-]", "").Trim();

                if (modifiedBio.Length <= 15)
                {
                    // Take away even more if their entire bio was "non-bio" information
                    scoreDbl -= 10;
                    negativeFilters.Add("pflWorthlessBio");
                }
                else
                {
                    // Goals:
                    //   Reward effort
                    //   Punish lack of effort
                    //   Keep other factors relevant

                    lengthModifier = Math.Pow(
                        modifiedBio.Length,
                        1.0 / 3
                    ) * 2.25 - 8;

                    if (lengthModifier >= 5)
                        positiveFilters.Add("pflBioLength");
                    bioScore += lengthModifier;
                    scoreDbl += bioScore;
                }
            }
            #endregion

            #region Interests
            if (rec.Interests == null)
            {
                // Top Picks don't list interests
                if (!rec.IsTopPick)
                {
                    // Again, a lack of effort
                    // Punished more severely if bio is lacking
                    // No punishment if bio provides sufficient info

                    negativeFilters.Add("pflNoInterests");
                    if (modifiedBio.Length == 0)
                        scoreDbl -= 5;
                    else if (bioScore < 4)
                        scoreDbl -= 3;
                }
            }
            else
            {
                totalInterestValue += (rec.Interests.Length - 3);
                
                // If the bio is lacking, interests take up some slack
                var multiplier = Math.Pow(
                    2 - (emptyBio || bio.Length == 0 ? 0 :
                        Math.Log10(bio.Length) / _bioLengthLogCap),
                    2.0 / 3.0
                );

                var interestGroups = new Dictionary<string, List<InterestScore>>();
                foreach (var interest in rec.Interests)
                {
                    if (_interestScores.TryGetValue(interest, out InterestScore interestScore))
                    {
                        string filterName;
                        if (
                            // Check if a matched filter has the same purpose as this interest
                            !(
                                (_filterOverlap.TryGetValue(interest, out filterName) ||
                                _filterOverlap.TryGetValue(interestScore.Group, out filterName)) &&
                                (positiveFilters.Contains(filterName) || negativeFilters.Contains(filterName))
                            )
                        )
                        {
                            if (!isUpsell || !_upsellImmuneInterests.Contains(interest))
                            {
                                if (!String.IsNullOrEmpty(interestScore.Group))
                                {
                                    if (interestGroups.TryGetValue(interestScore.Group, out List<InterestScore> list))
                                    {
                                        list.Add(interestScore);
                                    }
                                    else
                                    {
                                        interestGroups.Add(interestScore.Group, new List<InterestScore>() { interestScore });
                                    }
                                }
                                else
                                {
                                    totalInterestValue += interestScore.Modifier;
                                }
                            }
                        }
                    }
                }

                if (interestGroups.Count > 1)
                {
                    foreach(var group in interestGroups)
                    {
                        // Apply only the most important modifier
                        totalInterestValue += group.Value.Count == 1 ?
                            group.Value[0].Modifier :
                            group.Value.OrderByDescending(i => Math.Abs(i.Modifier)).First().Modifier;
                    }
                }

                totalInterestValue *= multiplier;
                scoreDbl += totalInterestValue;
                if (totalInterestValue >= 5)
                    positiveFilters.Add("pflInterests");
                else if (totalInterestValue <= -5)
                    negativeFilters.Add("pflInterests");
            }
            #endregion

            // If there are no interests, filters are meant to take up slack
            if (MatchesFilters(rec, out int numFilterMatches))
                scoreDbl -= numFilterMatches * numFilterMatches * (rec.Interests == null ? 3 : 1.5);

            if (negativeFilters.Any())
            {
                if (!String.IsNullOrWhiteSpace(_curRecLog[1]))
                    _curRecLog[1] += ", ";
                else
                    _curRecLog[1] = "\tFILTER - ";
                _curRecLog[1] += String.Join(", ", negativeFilters);
            }

            if (positiveFilters.Any())
                _curRecLog[2] = "\tJUDGE - " + String.Join(", ", positiveFilters);

            score = Convert.ToInt32(Math.Round(scoreDbl));

            if ((score >= -12 && isTopPick) || (score >= -7 && rec.IsSuperlikeUpsell))
                // Don't hard pass attractive people
                score = Math.Max(3, score);
            if (negativeFilters.Contains("locFar") && score <= 30)
                // Don't superlike people far away
                scoreDbl = Math.Min(20, scoreDbl);

            if (score <= -50 || _ignore.Contains(rec.User.ID))
                // Tinder messed up and they shouldn't even be in my feed
                return RecJudgement.SuperHardPass;
            else if (score < -5)
                // No second chances
                return RecJudgement.HardPass;
            else if (score < 5)
                // Potential second chance if there aren't enough right-swipes
                return RecJudgement.Pass;
            else if (score <= 15)
                return RecJudgement.Like;
            else if (score <= 25)
                // If we have too many right-swipes, like this recommendation anyway
                return RecJudgement.SkipImmunity;
            else if (score <= 30)
                return RecJudgement.Superlike;
            else
                // They are untouchable by any hardcoded filters
                return RecJudgement.FilterImmunity;
        }

        /// <summary>
        /// Sometimes people will mention something that trips a negative filter, when they are in fact agreeing with it.
        /// </summary>
        private static readonly SortedSet<string> _mistakenOpposites = new SortedSet<string>()
        {
            "arrPoly", "arrSell"
        };

        /// <summary>
        /// These filters are ignored if the profile is popular.
        /// </summary>
        private static readonly SortedSet<string> _upsellImmuneFilters = new SortedSet<string>()
        {
            "pflCatfish", "arrSell"
        };

        /// <summary>
        /// Checks to see how many no-nos a recommendation has in their profile.
        /// Handles any exceptions, conflicts, or non-bio filters.
        /// </summary>
        /// <param name="rec">The recommendation.</param>
        /// <param name="numMatches">How many filters were matched.</param>
        /// <returns>Whether it matched any filters.</returns>
        private static bool MatchesFilters(CompactRec rec, out int numMatches)
        {
            bool filtered = false;
            var profile = rec.User;
            string bio = profile.Biography;
            SortedSet<string> filtersMatched = new SortedSet<string>();

            // If they have little evidence that they are a real person
            if (rec.Instagram == null && (profile.Badges == null || !profile.Badges.Any()) && profile.Photos.Length < 4)
            {
                filtersMatched.Add("pflCatfish");
                filtered = true;
            }

            if (rec.Distance.HasValue && rec.Distance > 35)
            {
                filtersMatched.Add("pflDistance");
                filtered = true;
            }

            if (!String.IsNullOrWhiteSpace(bio))
            {
                foreach (var filter in _filters)
                {
                    if (filter.Value.Match(bio))
                        filtersMatched.Add(filter.Key);
                }
            }

            if (rec.IsSuperlikeUpsell)
                filtersMatched.ExceptWith(_upsellImmuneFilters);

            if (Regex.IsMatch(bio, "(?:don'?t|do not) (?:message|msg)|not (?:trying|tryna)"))
                filtersMatched.ExceptWith(_mistakenOpposites);

            // If they're a teacher then they might trigger the filter without having kids
            // If they're older then their kids are older
            if (
                filtersMatched.Contains("rgxKids") && (
                    bio.IContains("teacher") ||
                    profile.Birthday.Year >= 1990
                )
            )
                filtersMatched.Remove("rgxKids");

            numMatches = filtersMatched.Count;
            if (filtersMatched.Any())
            {
                filtered = true;
                _curRecLog[1] = "\tFILTER - " + String.Join(", ", filtersMatched);
            }
            return filtered;
        }
        #endregion

        #region Queries
        /// <summary>
        /// A query that finds the most common interests for upsells and for non-upsells.
        /// </summary>
        private static void GetTopInterests()
        {
            List<string> upsellInterests = new List<string>();
            List<string> othersInterests = new List<string>();
            int upsellNoInterestsCount = 0;
            int othersNoInterestsCount = 0;
            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                if (rec.Interests != null)
                {
                    if (rec.IsSuperlikeUpsell)
                        upsellInterests.AddRange(rec.Interests);
                    else
                        othersInterests.AddRange(rec.Interests);
                }
                else
                {
                    if (rec.IsSuperlikeUpsell)
                        ++upsellNoInterestsCount;
                    else
                        ++othersNoInterestsCount;
                }
            }

            Console.WriteLine("{0}, {1}", upsellInterests.Count, othersInterests.Count);
            Console.WriteLine();

            var othersInterestsGrouped = othersInterests.GroupBy(
                i => i,
                (k, g) => new KeyValuePair<string, int>(k, g.Count())
            ).ToDictionary(kv => kv.Key, kv => kv.Value);
            string[] otherInterestsKeys = othersInterestsGrouped.OrderByDescending(
                i => i.Value
            ).Select(kv => kv.Key).ToArray();

            var upsellInterestsGrouped = upsellInterests.GroupBy(
                i => i,
                (k, g) => new KeyValuePair<string, int>(k, g.Count())
            ).OrderByDescending(i => i.Value);

            Console.WriteLine(
                "{0, 4}, {1, 4} - No Interests",
                upsellNoInterestsCount,
                othersNoInterestsCount
            );

            int index = 1;
            foreach (var interest in upsellInterestsGrouped)
            {
                Console.WriteLine(
                    "{0,4}, {1,4} - {2}",
                    index++,
                    Array.IndexOf(otherInterestsKeys, interest.Key) + 1,
                    interest.Key
                );
            }
            Console.ReadLine();
        }

        /// <summary>
        /// Runs a filter on all seen recommendations and outputs the info of matches to a file.
        /// </summary>
        /// <param name="filter">Determines which recommendations match the filter.</param>
        /// <param name="outputFile">The file that the recommendations' info is written to.</param>
        private static void RunQuery(Func<CompactRec, bool> filter, string outputFile)
        {
            if (!Regex.IsMatch(outputFile, @"\.\w{1,4}$"))
                outputFile += ".txt";

            StreamWriter detailsWriter = new StreamWriter(PATH_QUERIES + outputFile);
            int numMatches = 0;
            double total = 0;
            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                if (!_ignore.Contains(rec.User.ID))
                {
                    ++total;
                    bool isMatch = filter(rec);
                    if (isMatch)
                    {
                        ++numMatches;
                        WriteReadableInfoTo(rec, detailsWriter, false);
                    }
                }
            }
            detailsWriter.Close();
            if (numMatches == 0)
                File.Delete(PATH_QUERIES + outputFile);

            Console.WriteLine("Num Matches: {0} ({1:#.0000})", numMatches, numMatches / total);
            Console.ReadLine();
        }

        /// <summary>
        /// Writes the most relevant details of a recommendation to a stream.
        /// </summary>
        /// <param name="rec">The recommendation.</param>
        /// <param name="detailsWriter">The stream.</param>
        /// <param name="writeRecLog">Whether to add the current contents of the recommendation scoring log.</param>
        private static void WriteReadableInfoTo(CompactRec rec, StreamWriter detailsWriter, bool writeRecLog)
        {
            detailsWriter.WriteLine();
            if (writeRecLog)
            {
                foreach (var line in _curRecLog)
                {
                    if (!String.IsNullOrWhiteSpace(line))
                        detailsWriter.WriteLine(line);
                }
                detailsWriter.WriteLine();
            }

            foreach(var pic in rec.User.Photos)
            {
                detailsWriter.WriteLine("https://images-ssl.gotinder.com/{0}/{1}", rec.User.ID, pic);
            }

            detailsWriter.WriteLine();

            if (!String.IsNullOrWhiteSpace(rec.User.City))
                detailsWriter.WriteLine(rec.User.City);
            if (rec.Distance.HasValue)
                detailsWriter.WriteLine(rec.Distance.Value.ToString() + " miles");

            detailsWriter.WriteLine();

            if (rec.IsTopPick || rec.IsSuperlikeUpsell)
            {
                if (rec.IsTopPick)
                    detailsWriter.WriteLine("Top Pick");
                if (rec.IsSuperlikeUpsell)
                    detailsWriter.WriteLine("Superlike Upsell");
                detailsWriter.WriteLine();
            }

            if (rec.Interests != null)
            {
                foreach (var interest in rec.Interests)
                {
                    detailsWriter.WriteLine("+\t" + interest);
                }
            }
            else
                detailsWriter.WriteLine("NO INTERESTS");

            detailsWriter.WriteLine();

            detailsWriter.WriteLine(rec.User.Biography);

            detailsWriter.WriteLine();
            detailsWriter.WriteLine("~~~~~~~~~~");
            detailsWriter.WriteLine();
        }
        #endregion

        #region Review
        /// <summary>
        /// Rescores all recommendations and updates the list of superlike worthy recommendations.
        /// Displays how many recommendations maintained, gained, or lost superlike worthy status.
        /// Writes the ids of recommendations with changed status to query files.
        /// </summary>
        private static void RebuildSuperlikeWorthy()
        {
            int index = 0;

            SortedSet<string> prevWorthy = new SortedSet<string>(SuperlikeWorthy);
            SuperlikeWorthy.Clear();
            _superlikeWorthy.Close();
            File.Delete(PATH_HISTORY + "SuperlikeWorthy.txt");
            _superlikeWorthy = new StreamWriter(PATH_HISTORY + "SuperlikeWorthy.txt") { AutoFlush = false };

            Stopwatch timer = Stopwatch.StartNew();
            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                int score = RecheckRec(rec, false, out RecJudgement judgement);
                if (judgement == RecJudgement.Superlike || judgement == RecJudgement.FilterImmunity)
                {
                    SuperlikeWorthy.Add(rec.User.ID);
                    _superlikeWorthy.WriteLine(rec.User.ID);
                }

                ++index;
                if (index % 1000 == 0)
                    Console.WriteLine("{0} users rechecked, {1} ms", index, timer.ElapsedMilliseconds);
            }

            _superlikeWorthy.Flush();
            timer.Stop();
            Console.WriteLine("{0} users rechecked, {1} ms", index, timer.ElapsedMilliseconds);
            Console.WriteLine();

            var upgraded = SuperlikeWorthy.Except(prevWorthy);
            var downgraded = prevWorthy.Except(SuperlikeWorthy);

            if (!Directory.Exists(PATH_QUERIES))
            {
                Directory.CreateDirectory(PATH_QUERIES);
            }
            File.WriteAllLines(PATH_QUERIES + "upgraded.txt", upgraded);
            File.WriteAllLines(PATH_QUERIES + "downgraded.txt", downgraded);

            Console.WriteLine("Worthy previously:   {0}", prevWorthy.Count);
            Console.WriteLine("Worthy now:          {0}", SuperlikeWorthy.Count);
            Console.WriteLine("Stayed worthy:       {0}", prevWorthy.Intersect(SuperlikeWorthy).Count());
            Console.WriteLine("Stayed unworthy:     {0}", UserHistory.Except(SuperlikeWorthy.Union(prevWorthy)).Count());
            Console.WriteLine("Upgraded:            {0}", upgraded.Count());
            Console.WriteLine("Downgraded:          {0}", downgraded.Count());
        }

        /// <summary>
        /// Rescores all recommendations and displays the rates of each judgement category.
        /// </summary>
        private static void ListJudgementRates()
        {
            int superHardPasses = 0;
            int hardPasses = 0;
            int passes = 0;
            int likes = 0;
            int skipImmune = 0;
            int superlikeWorthy = 0;
            int filterImmune = 0;
            int superlikeUpsell = 0;
            int topPick = 0;
            int index = 0;

            if (!Directory.Exists(PATH_QUERIES))
            {
                Directory.CreateDirectory(PATH_QUERIES);
            }

            Stopwatch timer = Stopwatch.StartNew();
            _scores.Close();
            File.Delete(PATH_HISTORY + "Scores.txt");
            _scores = new StreamWriter(PATH_HISTORY + "Scores.txt") { AutoFlush = true };
            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                int score = RecheckRec(rec, false, out RecJudgement judgement);
                if (rec.IsSuperlikeUpsell)
                    ++superlikeUpsell;
                if (TopPicks.Contains(rec.GetCustomID()))
                    ++topPick;

                _scores.WriteLine("{0}: {1} ({2})", rec.User.ID, score, judgement);

                switch (judgement)
                {
                    case RecJudgement.SuperHardPass:
                        ++superHardPasses;
                        break;

                    case RecJudgement.HardPass:
                        ++hardPasses;
                        break;

                    case RecJudgement.Pass:
                        ++passes;
                        break;

                    case RecJudgement.Like:
                        ++likes;
                        break;

                    case RecJudgement.SkipImmunity:
                        ++skipImmune;
                        break;

                    case RecJudgement.Superlike:
                        ++superlikeWorthy;
                        break;

                    case RecJudgement.FilterImmunity:
                        ++filterImmune;
                        break;
                }

                ++index;
                if (index % 1000 == 0)
                    Console.WriteLine("{0} users rechecked, {1} ms", index, timer.ElapsedMilliseconds);
            }

            timer.Stop();

            Console.WriteLine("{0} users rechecked, {1} ms", index, timer.ElapsedMilliseconds);
            Console.WriteLine();

            double total = superHardPasses + hardPasses + passes + likes + skipImmune + superlikeWorthy + filterImmune;
            Console.WriteLine("Total:            {0}", Convert.ToInt32(total));
            Console.WriteLine("Superlike Upsell: {0} ({1:#.0000})", superlikeUpsell, superlikeUpsell / total);
            Console.WriteLine("Top Picks:        {0} ({1:#.0000})", topPick, topPick / total);
            Console.WriteLine("Super Hard Passes:{0} ({1:#.0000})", superHardPasses, superHardPasses / total);
            Console.WriteLine("Hard Passes:      {0} ({1:#.0000})", hardPasses, hardPasses / total);
            Console.WriteLine("Passes:           {0} ({1:#.0000})", passes, passes / total);
            Console.WriteLine("Likes:            {0} ({1:#.0000})", likes, likes / total);
            Console.WriteLine("Skip Immune:      {0} ({1:#.0000})", skipImmune, skipImmune / total);
            Console.WriteLine("Superlike Worthy: {0} ({1:#.0000})", superlikeWorthy, superlikeWorthy / total);
            Console.WriteLine("Filter Immune:    {0} ({1:#.0000})", filterImmune, filterImmune / total);
        }

        #region RecheckRec
        /// <summary>
        /// Rescores a single recommendation.
        /// </summary>
        /// <param name="id">The id of the recommendation to rescore.</param>
        /// <param name="doLog">Whether to display the log for the scoring.</param>
        /// <param name="score">The calculated score of the recommendation.</param>
        /// <returns>The recommendation object.</returns>
        private static CompactRec RecheckRec(string id, bool doLog, out int score) =>
            RecheckRec(id, doLog, out score, out _);

        /// <summary>
        /// Rescores a single recommendation.
        /// </summary>
        /// <param name="id">The id of the recommendation to rescore.</param>
        /// <param name="doLog">Whether to display the log for the scoring.</param>
        /// <param name="score">The calculated score of the recommendation.</param>
        /// <param name="judgement">The judgement category based on the score.</param>
        /// <returns>The recommendation object.</returns>
        private static CompactRec RecheckRec(string id, bool doLog, out int score, out RecJudgement judgement)
        {
            var rec = Utils.LoadJSON<CompactRec>(PATH_USERS + id + ".json", API._opts);
            score = RecheckRec(rec, doLog, out judgement);
            return rec;
        }

        /// <summary>
        /// Rescores a single recommendation.
        /// </summary>
        /// <param name="rec">The recommendation to rescore.</param>
        /// <param name="doLog">Whether to display the log for the scoring.</param>
        /// <returns>The calculated score of the recommendation.</returns>
        private static int RecheckRec(CompactRec rec, bool doLog) =>
            RecheckRec(rec, doLog, out _);

        /// <summary>
        /// Rescores a single recommendation.
        /// </summary>
        /// <param name="rec">The recommendation to rescore.</param>
        /// <param name="doLog">Whether to display the log for the scoring.</param>
        /// <param name="judgement">The judgement category based on the score.</param>
        /// <returns>The calculated score of the recommendation.</returns>
        private static int RecheckRec(CompactRec rec, bool doLog, out RecJudgement judgement)
        {
            judgement = Judge(
                rec,
                TopPicks.Contains(rec.GetCustomID()),
                out int score
            );

            if (doLog)
            {
                _curRecLog[0] = String.Format(
                    "{0} - {1} with score {2}, for testing",
                    (int)judgement > 0 ? "LIKE" : "PASS",
                    rec.User.ID, score
                );

                WriteCurRecLog();
            }

            return score;
        }
        #endregion
        #endregion
    }
}
