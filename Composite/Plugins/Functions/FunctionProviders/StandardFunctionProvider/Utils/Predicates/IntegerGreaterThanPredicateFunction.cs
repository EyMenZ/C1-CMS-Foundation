﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Data.SqlTypes;

using Composite.Functions;

using Composite.Plugins.Functions.FunctionProviders.StandardFunctionProvider.Foundation;
using Composite.Core.ResourceSystem;
using System.Linq.Expressions;

namespace Composite.Plugins.Functions.FunctionProviders.StandardFunctionProvider.Utils.Predicates
{
    internal sealed class IntegerGreaterThanPredicateFunction : StandardFunctionBase
    {
        public IntegerGreaterThanPredicateFunction(EntityTokenFactory entityTokenFactory)
            : base("IntegerGreaterThan", "Composite.Utils.Predicates", typeof(Expression<Func<int, bool>>), entityTokenFactory)
        {
        }


        protected override IEnumerable<StandardFunctionParameterProfile> StandardFunctionParameterProfiles
        {
            get
            {
                WidgetFunctionProvider widget = StandardWidgetFunctions.IntegerTextBoxWidget;

                yield return new StandardFunctionParameterProfile(
                    "Value", typeof(int), true, new NoValueValueProvider(), widget);
            }
        }


        public override object Execute(ParameterList parameters, FunctionContextContainer context)
        {
            int value = parameters.GetParameter<int>("Value");
            Expression<Func<int,bool>> predicate = f=>f > value;
            return predicate;
        }
    }
}
