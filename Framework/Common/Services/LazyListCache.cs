using System;
using System.Collections.Generic;

namespace TM.Framework.Common.Services
{
    public sealed class LazyListCache<T>
    {
        private List<T>? _cache;

        public List<T> Get(Func<List<T>> factory)
            => _cache ??= factory();

        public void Invalidate() => _cache = null;

        public bool IsEmpty => _cache == null;
    }
}
