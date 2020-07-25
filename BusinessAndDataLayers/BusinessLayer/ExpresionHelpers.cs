﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace BusinessLayerLib
{
    public static class ExpresionHelpers
    {
        public static Expression CreateQueryExpression<T>(this GetAsync getAsync, Expression<Func<T, bool>> predicate)
        {
            return predicate;
        }

        public async static Task<IAsyncEnumerable<T>> GetAsync<T>(this GetAsync getAsync, Expression<Func<T, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return (await getAsync(predicate)).Cast<T>();
        }
    }
}