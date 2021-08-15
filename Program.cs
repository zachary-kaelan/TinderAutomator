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
        private static readonly Regex RGX_MyersBriggs = new Regex(
               @"[IE][NS][FT][JP]",
               RegexOptions.IgnoreCase | RegexOptions.Compiled
           );
        private static readonly Regex RGX_Kids = new Regex(
            @"\s[1-7] ?(?:yo|yr old|year old)(?:\s|$|\.|,)|" +
            @" a (?:son|daughter|mom|mother|kid|child)|" +
            @"single (?:mom|mother)|" +
            @"(?:mom|mother) of (?:one|two|three)(?:[\.,\r\n]|$)|" +
            @"comes? first|" +
            @"(?:have(?: a)?|my) (?:kid|child)|" +
            @"little (?:boy|girl)|" +
            @"if you (?:don'?t|do not) like kid",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        private static readonly Regex RGX_Height = new Regex(
            @"(?:^|\D)[4-6]'\d{1,2}(?:\D|$|\.)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
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

        private const string PATH_HISTORY = @"History\";
        private const string PATH_LOGS = @"Logs\";
        private const string PATH_USERS = @"Users\";
        private const string PATH_TOP_PICKS = @"Top Picks\";
        private const string PATH_DEETS = @"Deets\";
        static Program()
        {
            if (!Directory.Exists(PATH_HISTORY))
                Directory.CreateDirectory(PATH_HISTORY);
            if (!Directory.Exists(PATH_LOGS))
                Directory.CreateDirectory(PATH_LOGS);
            if (!Directory.Exists(PATH_USERS))
                Directory.CreateDirectory(PATH_USERS);
            if (!Directory.Exists(PATH_TOP_PICKS))
                Directory.CreateDirectory(PATH_TOP_PICKS);
            if (!Directory.Exists(PATH_DEETS))
                Directory.CreateDirectory(PATH_DEETS);

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
            _hotties = new StreamWriter(PATH_HISTORY + "SuperlikeUpsells.txt", true) { AutoFlush = true };
        }

        private static readonly SortedDictionary<string, BaseRecFilter> _filters = new SortedDictionary<string, BaseRecFilter>(
            new BaseRecFilter[] {
                new ArrayRecFilter(
                    "arrAstrology",
                    ARR_AstrologySigns
                ),
                new RegexRecFilter(
                    "rgxKids", RGX_Kids
                ),
                new RegexRecFilter(
                    "rgxSocialMedia",
                    Recommendation.RGX_SocialMedia
                ),
                new ArrayRecFilter(
                    "arrAbsent",
                    new string[]
                    {
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
                )
            }.ToDictionary(f => f.Name, f => f)
        );

        private static readonly KeyValuePair<BaseRecFilter, int>[] _positiveFilters = new KeyValuePair<BaseRecFilter, int>[]
        {
            
        };

        private static readonly SortedSet<string> _activeInterests = new SortedSet<string>()
        {
            "Athlete",
            "Working out",
            "Sports",
            "Running"
        };

        private static readonly SortedSet<string> _hottieImmuneInterests = new SortedSet<string>()
        {

        };

        private static readonly SortedDictionary<string, int> _interestScores = new SortedDictionary<string, int>()
        {

        };

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
        private static StreamWriter _hotties;

        private static SortedSet<string> UserHistory;
        private static SortedSet<string> TopPicks;
        private static SortedSet<string> Skipped;
        private static SortedSet<string> SuperlikeWorthy;

        private static DateTime? LikesRefresh = null;
        private static DateTime? SuperlikeRefresh = null;
        private static DateTime? TopPicksRefresh = null;

        static void Main(string[] args)
        {
            // Console.WindowWidth = 120;
            // Console.WindowHeight = 72;

            TopPicks = File.Exists(PATH_HISTORY + "TopPicks.txt") ?
                new SortedSet<string>(File.ReadLines(PATH_HISTORY + "TopPicks.txt")) :
                new SortedSet<string>();
            _topPicks = new StreamWriter(PATH_HISTORY + "TopPicks.txt", true) { AutoFlush = true };

            UserHistory = File.Exists(PATH_HISTORY + "UserHistory.txt") ?
                new SortedSet<string>(File.ReadLines(PATH_HISTORY + "UserHistory.txt")) :
                new SortedSet<string>();
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
            SuperlikeWorthy.ExceptWith(Skipped);

            _scores = new StreamWriter(PATH_HISTORY + "Scores.txt", true) { AutoFlush = true };
            var rankedSuperlikeWorthy = SuperlikeWorthy.Select(
                s =>
                {
                    var rec = RecheckRec(s, false, out int score);
                    return new KeyValuePair<int, string>(score, s);
                }
            ).OrderByDescending(s => s.Key);
            Console.WriteLine();
            SetCooldowns();
            foreach (var superlikeWorthy in rankedSuperlikeWorthy)
            {
                if (superlikeWorthy.Key > 25)
                {
                    if (UseSuperlike(superlikeWorthy.Value))
                        break;
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
                    _today = DateTime.Now;
                    _log.Close();
                    _log = new StreamWriter(PATH_LOGS + _today.ToString("yyyy'-'MM'-'dd") + ".txt", true) { AutoFlush = true };
                }
                SpinWait.SpinUntil(() => DateTime.Now > LikesRefresh.Value);
                LikesRefresh = null;
                API.GetNewAuthToken();
            }
        }

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

        private static string[] _curRecLog = new string[4];

        private static void WriteCurRecLog()
        {
            foreach (var line in _curRecLog)
            {
                if (!String.IsNullOrWhiteSpace(line))
                    Log(line);
            }
            Array.Clear(_curRecLog, 0, 4);
        }

        private static int RunLoop()
        {
            Array.Clear(_curRecLog, 0, 4);
            int numSwipes = 0;
            var skippedToLike = Skipped.Except(SuperlikeWorthy).ToArray();
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
                    _curRecLog[4] = "MATCH - IT'S A MATCH!!! :O :O :O";

                WriteCurRecLog();

                Thread.Sleep(5000);

                if (LikesRefresh != null)
                    return numSwipes;
            }

            foreach (var recTemp in API.GetRecommendations())
            {
                if (LikesRefresh != null)
                    return numSwipes;

                if (
                    Skipped.Contains(recTemp.User.ID) || 
                    SuperlikeWorthy.Contains(recTemp.User.ID)
                ) {
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
                _scores.WriteLine(rec.User.ID + ":" + score.ToString());
                bool isMatch = false;
                var now = DateTime.Now;

                if (isTopPick || rec.IsSuperlikeUpsell)
                    _hotties.WriteLine(rec.User.ID);

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
                    _curRecLog[4] = "MATCH - IT'S A MATCH!!! :O :O :O";
                WriteCurRecLog();

                Thread.Sleep(5000);

                if (numSwipes % 15 == 0)
                    Log("RATIO - Right swipe ratio is {0}", rightSwipeRatio);

                CheckTopPicks();

                UseSuperlike();
            }

            return numSwipes;
        }

        private static void CheckTopPicks()
        {
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
                    // gonna superlike this bitch
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
                    Log("\tMATCH - IT'S A MATCH!!! :O :O :O");

                TopPicksRefresh = Utils.ConvertUnixTimestamp(topPicksResponse.TopPicksRefreshTime);
                UserHistory.Add(bestTopPickScore.Key.User.ID);
                _history.WriteLine(bestTopPickScore.Key.User.ID);
            }
        }

        private static string UseSuperlike()
        {
            if (SuperlikeWorthy.Count > 0 && (SuperlikeRefresh == null || DateTime.Now > SuperlikeRefresh.Value))
            {
                var topSuperlikeRec = RankSuperlikeWorthy();

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
                        Log("MATCH - IT'S A MATCH!!! :O :O :O");

                    return id;
                }
            }

            return null;
        }

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

        private static KeyValuePair<CompactRec, int> RankSuperlikeWorthy()
        {
            Array.Clear(_curRecLog, 0, 4);
            SortedDictionary<long, string[]> recLogs = new SortedDictionary<long, string[]>();
            List<KeyValuePair<CompactRec, int>> recScores = new List<KeyValuePair<CompactRec, int>>();
            SortedSet<string> toRemove = new SortedSet<string>();
            var now = DateTime.Now;
            foreach (var superlikeRecID in SuperlikeWorthy)
            {
                var superlikeRec = Utils.LoadJSON<CompactRec>(PATH_USERS + superlikeRecID + ".json", API._opts);
                if (
                    (
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

                if ((superlikeRecJudgement != RecJudgement.Superlike &&
                    superlikeRecJudgement != RecJudgement.FilterImmunity) || 
                    superlikeRecScore < 15)
                {
                    toRemove.Add(superlikeRecID);
                    continue;
                }

                if (TopPicks.Contains(thisTopPickString))
                    superlikeRec.IsTopPick = true;

                _curRecLog[0] = String.Format(
                    "{0} with score {1}",
                    superlikeRecID,
                    superlikeRecScore
                );
                var curSuperlikeRecLog = new string[4];
                Array.Copy(_curRecLog, curSuperlikeRecLog, 4);
                recLogs.Add(superlikeRec.SNumber, curSuperlikeRecLog);
                Array.Clear(_curRecLog, 0, 4);

                recScores.Add(new KeyValuePair<CompactRec, int>(superlikeRec, superlikeRecScore));
            }

            SuperlikeWorthy.ExceptWith(toRemove);
            foreach(var recId in toRemove)
            {
                if (!Skipped.Contains(recId))
                {
                    _skipped.WriteLine(recId);
                    Skipped.Add(recId);
                }
            }

            if (recScores.Any())
            {
                FileStream detailsFile = new FileStream(
                    "Superlikes Review\\" + DateTime.Now.ToString("MM''dd'-'hh") + ".txt",
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
                    if (!toRemove.Contains(score.Key.User.ID))
                    {
                        var rec = score.Key;
                        WriteDeetsTo(rec, detailsWriter, true);
                    }
                    WriteCurRecLog();
                }

                detailsWriter.Close();
                detailsFile.Close();

                Console.WriteLine();
                return recScores[0];
            }
            else
                return new KeyValuePair<CompactRec, int>();
        }

        private static void Log(string format, params object[] args) =>
            Log(String.Format(format, args));

        private static void Log(string line)
        {
            _log.WriteLine(line);
            Console.WriteLine(line);
        }

        private static readonly double _bioLengthLogCap = Math.Log10(500);

        private static RecJudgement Judge(CompactRec rec, bool isTopPick, out int score)
        {
            double scoreDbl = 5;  // a base amount of 5, for being a girl
            double bioScore = 0;
            double locCloseScore = 0;
            double lengthModifier = 0;
            double totalInterestValue = 0;

            string bio = rec.User.Biography;
            bool emptyBio = String.IsNullOrWhiteSpace(bio);
            if (!emptyBio)
                bio = bio.Replace('‘', '\'');
            string modifiedBio = bio;

            bool isMegaHottie = isTopPick || rec.IsSuperlikeUpsell;

            if (!isTopPick && emptyBio && rec.Interests == null && rec.Vibes == null)
            {
                _curRecLog[1] = "\tFILTER - No info";
                if (isMegaHottie && rec.Distance.HasValue && rec.Distance.Value <= 50)
                {
                    score = 3;
                    _curRecLog[2] = "\tJUDGE - " + (
                        rec.IsSuperlikeUpsell ?
                            "pflSuperUpsell" :
                            "pflTopPick"
                        );
                    return RecJudgement.Pass;
                }
                else
                {
                    score = -50;
                    return RecJudgement.SuperHardPass;
                }
            }

            bool isHottie = isMegaHottie || (
                rec.Interests != null && rec.Interests.Any() && (
                    rec.Interests.Contains("Working out") ||
                    rec.Interests.Contains("Athlete") ||
                    bio.IContains("gym") ||
                    bio.IContains("work out") ||
                    bio.IContains("working out")
                )
            );

            SortedSet<string> filtersMatched = new SortedSet<string>();
            SortedSet<string> negativeFilters = new SortedSet<string>();

            if (!String.IsNullOrWhiteSpace(rec.User.City))
            {
                if (rec.User.City == "Mine")
                    locCloseScore += 5;
                else if (rec.User.City == "Adjacent")
                    locCloseScore += 3;
                else if (
                    rec.User.City == "Farther1" ||
                    rec.User.City == "Farther2"
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
                }
            }

            if (locCloseScore > 0)
            {
                if (
                    bio.IContains("for the summer") ||
                    bio.IContains("for this summer") ||
                    filtersMatched.Contains("locFar")
                )
                    locCloseScore = 0;
                locCloseScore = Math.Sqrt(locCloseScore);
                if (locCloseScore >= 2.5)
                    filtersMatched.Add("locClose");
                locCloseScore = Math.Min(3, locCloseScore);
                scoreDbl += locCloseScore;
            }

            if (isHottie)
            {
                if (isTopPick)
                {
                    scoreDbl += 5; // hot and picked for me
                    filtersMatched.Add("pflTopPick");
                }
                else if (rec.IsSuperlikeUpsell)
                    scoreDbl += 4;
                else
                {
                    filtersMatched.Add("pflAthlete");
                    scoreDbl += 3;
                }

                // if they're far then being hot is less relevant
                if (filtersMatched.Contains("locFar"))
                    scoreDbl -= 2;
            }

            if (rec.IsSuperlikeUpsell)
                filtersMatched.Add("pflSuperUpsell");

            int positiveFiltersScore = 0;
            if (emptyBio)
            {
                scoreDbl -= 7; // no effort
                negativeFilters.Add("pflEmptyBio");
            }
            else
            {
                foreach (var filter in _positiveFilters)
                {
                    if (filter.Key.Match(bio))
                    {
                        filtersMatched.Add(filter.Key.Name);
                        positiveFiltersScore += filter.Value;
                    }
                }

                scoreDbl += positiveFiltersScore;

                modifiedBio = Recommendation.RGX_SocialMedia.Replace(modifiedBio, "");
                modifiedBio = RGX_MyersBriggs.Replace(modifiedBio, "");
                modifiedBio = RGX_Height.Replace(modifiedBio, "");
                foreach(var sign in ARR_AstrologySigns)
                {
                    modifiedBio = modifiedBio.Replace(sign, "");
                }
                modifiedBio = modifiedBio.Trim();

                modifiedBio = Regex.Replace(modifiedBio, @"\n{2,}", "\n").Trim(); 
                bioScore += Math.Sqrt(modifiedBio.ICount("\n"));

                modifiedBio = Regex.Replace(modifiedBio, "[^A-Za-z',!?()\\\\/-]", "");
                modifiedBio = Regex.Replace(modifiedBio, @"\?{2,}", "?");
                modifiedBio = Regex.Replace(modifiedBio, @"\!{2,}", "!").Trim();

                bioScore += modifiedBio.ICount("?") * 2;       // asking questions

                if (bioScore >= 4)
                    filtersMatched.Add("pflFormatting");

                if (modifiedBio.Length <= 15)
                {
                    // take away even more if their whole profile is faff
                    scoreDbl -= 10;
                    negativeFilters.Add("pflWorthlessBio");
                }
                else
                {
                    lengthModifier = Math.Pow(
                        modifiedBio.Length,
                        1.0 / 3
                    ) * 2.25 - 8;
                    if (lengthModifier >= 5)
                        filtersMatched.Add("pflBioLength");
                    bioScore += lengthModifier;
                    scoreDbl += bioScore;
                }
            }

            if (rec.Interests == null)
            {
                if (!rec.IsTopPick)
                {
                    negativeFilters.Add("pflNoInterests");
                    if (modifiedBio.Length == 0)
                        scoreDbl -= 5;
                    else if (modifiedBio.Length < 125)
                        scoreDbl -= 3;
                }
            }
            else
            {
                totalInterestValue += (rec.Interests.Length - 3);
                // if the bio is shit then this is all we have to go on
                var multiplier = Math.Pow(
                    2 - (emptyBio || bio.Length == 0 ? 0 :
                        Math.Log10(bio.Length) / _bioLengthLogCap),
                    2.0 / 3.0
                );

                SortedSet<string> activeInterests = new SortedSet<string>();
                foreach (var interest in rec.Interests)
                {
                    if (_interestScores.TryGetValue(interest, out int interestScore))
                    {
                        if (!isHottie || !_hottieImmuneInterests.Contains(interest))
                        {
                            totalInterestValue += interestScore;
                            if (_activeInterests.Contains(interest) && !isHottie)
                                activeInterests.Add(interest);
                        }
                    }
                }

                if (activeInterests.Count > 1)
                {
                    int max = activeInterests.Max(i => _interestScores[i]);
                    foreach(var activeInterest in activeInterests)
                    {
                        totalInterestValue -= _interestScores[activeInterest];
                    }
                    totalInterestValue += max + (activeInterests.Count - 1);
                }

                totalInterestValue *= multiplier;
                scoreDbl += totalInterestValue;
                if (totalInterestValue >= 5)
                    filtersMatched.Add("pflInterests");
                else if (totalInterestValue <= -5)
                    negativeFilters.Add("pflInterests");
            }

            var extras = ((
                !String.IsNullOrWhiteSpace(rec.User.Job) ||
                bio.IContains("working as") ||
                bio.IContains(" gig ") ||
                bio.IContains("work with") ||
                bio.IContains("working with")
            ) ? 1 : 0) + ((
                !String.IsNullOrWhiteSpace(rec.User.School) ||
                bio.IContains("studying") ||
                bio.IContains("degree") ||
                bio.IContains("associates") ||
                bio.IContains("major") ||
                Regex.IsMatch(bio, @" (?:['‘]2\d|202\d)(?:\s|$)")
            ) ? 1 : 0);
            scoreDbl += (extras + extras) * 1.5;
            if (extras == 2)
                filtersMatched.Add("pflGoals");

            if (MatchesFilters(rec, isHottie, out int numFilterMatches))
                scoreDbl -= numFilterMatches * numFilterMatches * (rec.Interests == null ? 3 : 1.5);

            if (negativeFilters.Any())
            {
                if (!String.IsNullOrWhiteSpace(_curRecLog[1]))
                    _curRecLog[1] += ", ";
                else
                    _curRecLog[1] = "\tFILTER - ";
                _curRecLog[1] += String.Join(", ", negativeFilters);
            }

            if (filtersMatched.Any())
                _curRecLog[2] = "\tJUDGE - " + String.Join(", ", filtersMatched);

            score = Convert.ToInt32(Math.Round(scoreDbl));

            if ((scoreDbl >= -12 && isTopPick) || (scoreDbl >= -7 && rec.IsSuperlikeUpsell))
                scoreDbl = Math.Max(3, scoreDbl);

                scoreDbl = Math.Min(20, scoreDbl);

            if (scoreDbl <= -50)
                return RecJudgement.SuperHardPass;
            else if (scoreDbl < -5)
                return RecJudgement.HardPass;
            else if (scoreDbl < 5)
                return RecJudgement.Pass;
            else if (scoreDbl <= 15)
                return RecJudgement.Like;
            else if (scoreDbl <= 25)
                return RecJudgement.SkipImmunity;
            else if (scoreDbl <= 30)
                return RecJudgement.Superlike;
            else
                return RecJudgement.FilterImmunity;
        }

        private static bool MatchesFilters(CompactRec rec, bool isHottie, out int numMatches)
        {
            bool filtered = false;
            var profile = rec.User;
            string bio = profile.Biography;
            SortedSet<string> filtersMatched = new SortedSet<string>();

            if (!String.IsNullOrWhiteSpace(bio))
            {
                foreach (var filter in _filters)
                {
                    if (filter.Value.Match(bio))
                        filtersMatched.Add(filter.Key);
                }
            }

            numMatches = filtersMatched.Count;
            if (filtersMatched.Any())
            {
                filtered = true;
                _curRecLog[1] = "\tFILTER - " + String.Join(", ", filtersMatched);
            }
            return filtered;
        }

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

        private static void CheckAgainstHotties(Func<CompactRec, bool> filter)
        {
            int hottiesMatches = 0;
            int numNonHotties = 0;
            int nonHottiesMatches = 0;
            int numHotties = 0;

            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                bool isMatch = filter(rec);
                if (rec.IsSuperlikeUpsell)
                    ++numHotties;
                else
                    ++numNonHotties;
                if (isMatch)
                {
                    if (rec.IsSuperlikeUpsell)
                        ++hottiesMatches;
                    else
                        ++nonHottiesMatches;
                }
            }

            int numTotal = numNonHotties + numHotties;
            int totalMatches = nonHottiesMatches + hottiesMatches;
            Console.WriteLine("Hottie Matches: {0}/{1} ({2:#.000})", hottiesMatches, numHotties, (double)hottiesMatches / numHotties);
            Console.WriteLine("Non-Hottie Matches: {0}/{1} ({2:#.000})", nonHottiesMatches, numNonHotties, (double)nonHottiesMatches / numNonHotties);
            Console.WriteLine("All Matches: {0}/{1} ({2:#.000})", totalMatches, numTotal, (double)totalMatches / numTotal); 
            Console.WriteLine("Normal Ratio: {0:#.000}", (double)numNonHotties / numHotties);
            Console.WriteLine("Match Ratio: {0:#.000}", (double)nonHottiesMatches / hottiesMatches);
            Console.ReadLine();
        }

        private static void CheckAgainstTopPicks(Func<CompactRec, bool> filter)
        {
            int hottiesMatches = 0;
            int numNonHotties = 0;
            int nonHottiesMatches = 0;
            int numHotties = 0;

            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                bool isMatch = filter(rec);
                bool isTopPick = TopPicks.Contains(rec.GetCustomID());
                if (isTopPick)
                    ++numHotties;
                else
                    ++numNonHotties;
                if (isMatch)
                {
                    if (isTopPick)
                        ++hottiesMatches;
                    else
                        ++nonHottiesMatches;
                }
            }

            int numTotal = numNonHotties + numHotties;
            int totalMatches = nonHottiesMatches + hottiesMatches;
            Console.WriteLine("Hottie Matches: {0}/{1} ({2:#.000})", hottiesMatches, numHotties, (double)hottiesMatches / numHotties);
            Console.WriteLine("Non-Hottie Matches: {0}/{1} ({2:#.000})", nonHottiesMatches, numNonHotties, (double)nonHottiesMatches / numNonHotties);
            Console.WriteLine("All Matches: {0}/{1} ({2:#.000})", totalMatches, numTotal, (double)totalMatches / numTotal);
            Console.WriteLine("Normal Ratio: {0:#.000}", (double)numNonHotties / numHotties);
            Console.WriteLine("Match Ratio: {0:#.000}", (double)nonHottiesMatches / hottiesMatches);
            Console.ReadLine();
        }

        private static void GetDeets(Func<CompactRec, bool> filter, string fileName)
        {
            if (!fileName.EndsWith(".txt"))
                fileName += ".txt";
            StreamWriter detailsWriter = new StreamWriter(PATH_DEETS + fileName);
            int numMatches = 0;
            double total = 0;
            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                ++total;
                bool isMatch = filter(rec);
                if (isMatch)
                {
                    ++numMatches;
                    WriteDeetsTo(rec, detailsWriter, false);
                }
            }
            detailsWriter.Close();
            if (numMatches == 0)
                File.Delete(PATH_DEETS + fileName);

            Console.WriteLine("Num Matches: {0} ({1:#.0000})", numMatches, numMatches / total);
            Console.ReadLine();
        }

        private static void WriteDeetsTo(CompactRec rec, StreamWriter detailsWriter, bool writeRecLog)
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

        private static void RebuildTopPicksList()
        {
            int numTopPicks = 0;
            int[] numPhotos = new int[11];
            foreach(var topPickPath in Directory.GetFiles(PATH_TOP_PICKS))
            {
                var rec = Utils.LoadJSON<Recommendation>(topPickPath, API._opts);
                if (!rec.IsTopPick)
                    continue;
                ++numTopPicks;
                var topPickString = rec.GetCustomID();
                _topPicks.WriteLine(topPickString);
                TopPicks.Add(topPickString);

                ++numPhotos[rec.User.Photos.Length];
            }

            Console.WriteLine(numTopPicks);
            for (int i = 1; i < 11; ++i)
            {
                Console.WriteLine("{0,2} - {1}", i, numPhotos[i]);
            }
        }

        private static SortedDictionary<string, int> RebuildSuperlikeWorthy()
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

            Stopwatch timer = Stopwatch.StartNew();
            SortedDictionary<string, int> scores = new SortedDictionary<string, int>();
            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                int score = RecheckRec(rec, false, out RecJudgement judgement);
                if (rec.IsSuperlikeUpsell)
                    ++superlikeUpsell;
                if (TopPicks.Contains(rec.GetCustomID()))
                    ++topPick;
                if (judgement == RecJudgement.Superlike || judgement == RecJudgement.FilterImmunity)
                {
                    SuperlikeWorthy.Add(rec.User.ID);
                    _superlikeWorthy.WriteLine(rec.User.ID);
                }
                scores.Add(rec.User.ID, score);

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

            Console.WriteLine("{0} users rechecked, {1} ms", index, timer.ElapsedMilliseconds);
            scores.SaveDictAs(PATH_HISTORY + "Scores.txt", ":");
            Console.WriteLine("Scores saved, {0} ms", timer.ElapsedMilliseconds);
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
            Console.ReadLine();

            return scores;
        }

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

            SortedDictionary<string, int> scores = new SortedDictionary<string, int>();
            Stopwatch timer = Stopwatch.StartNew();
            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                // var score = rec.Value;
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                int score = RecheckRec(rec, false, out RecJudgement judgement);
                if (rec.IsSuperlikeUpsell)
                    ++superlikeUpsell;
                if (TopPicks.Contains(rec.GetCustomID()))
                    ++topPick;
                scores.Add(rec.User.ID, score);

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


            Console.WriteLine("{0} users rechecked, {1} ms", index, timer.ElapsedMilliseconds);
            scores.SaveDictAs(PATH_HISTORY + "Scores.txt", ":");
            Console.WriteLine("Scores saved, {0} ms", timer.ElapsedMilliseconds);
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
            Console.ReadLine();
        }

        private static void GetAveragePhotoCount()
        {
            int numPhotos = 0;
            int index = 0;
            Stopwatch timer = Stopwatch.StartNew();
            foreach (var recPath in Directory.GetFiles(PATH_USERS))
            {
                var rec = Utils.LoadJSON<CompactRec>(recPath, API._opts);
                numPhotos += rec.User.Photos.Length;

                ++index;
                if (index % 1000 == 0)
                    Console.WriteLine("{0} users rechecked, {1} ms", index, timer.ElapsedMilliseconds);
            }


            Console.WriteLine("{0} users rechecked, {1} ms", index, timer.ElapsedMilliseconds);
            Console.WriteLine("{0} photos average", numPhotos / (index + 1.0));
            Console.ReadLine();
        }

        private static CompactRec RecheckRec(string id, bool doLog, out int score) =>
            RecheckRec(id, doLog, out score, out _);

        private static CompactRec RecheckRec(string id, bool doLog, out int score, out RecJudgement judgement)
        {
            var rec = Utils.LoadJSON<CompactRec>(PATH_USERS + id + ".json", API._opts);
            score = RecheckRec(rec, doLog, out judgement);
            return rec;
        }

        private static int RecheckRec(CompactRec rec, bool doLog) =>
            RecheckRec(rec, doLog, out _);

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
    }
}
