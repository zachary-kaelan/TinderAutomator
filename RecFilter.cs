using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TinderAutomator
{
    public enum RecFilterType
    {
        String,
        Regex,
        Array
    }

    public abstract class BaseRecFilter
    {
        public string Name { get; protected set; }
        protected RecFilterType _type { get; set; }
        protected static CompareInfo COMPARE = CultureInfo.InvariantCulture.CompareInfo;

        public BaseRecFilter(string name, RecFilterType type)
        {
            Name = name;
            _type = type;
        }

        public abstract bool Match(string bio);
    }

    public sealed class StringRecFilter : BaseRecFilter
    {
        private string _filter;

        public StringRecFilter(string name, string filter) : base(name, RecFilterType.String)
        {
            _filter = filter;
        }

        public override bool Match(string bio)
        {
            return COMPARE.IndexOf(bio, _filter, CompareOptions.IgnoreCase) != -1;
        }
    }

    public sealed class RegexRecFilter : BaseRecFilter
    {
        private Regex _filter;

        public RegexRecFilter(string name, Regex filter) : base(name, RecFilterType.Regex)
        {
            _filter = filter;
        }

        public override bool Match(string bio)
        {
            return _filter.IsMatch(bio);
        }
    }

    public sealed class ArrayRecFilter : BaseRecFilter
    {
        private string[] _filter;

        public ArrayRecFilter(string name, string[] filter) : base(name, RecFilterType.Array)
        {
            _filter = filter;
        }

        public override bool Match(string bio)
        {
            return _filter.Any(f => COMPARE.IndexOf(bio, f, CompareOptions.IgnoreCase) != -1);
        }
    }
}