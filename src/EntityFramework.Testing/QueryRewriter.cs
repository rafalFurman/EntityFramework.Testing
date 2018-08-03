//-----------------------------------------------------------------------------------------------------
// <copyright file="DefaultRewriter.cs" company="QDARC Rafał Furman">
// Copyright (c) Rafał Furman. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------------------------------------

namespace EntityFramework.Testing
{
    using System;
    using System.Collections.Generic;
    using System.Linq.Expressions;

    /// <summary>
    /// Extension point that allows rewriting of Expressions that are to be executed
    /// as linq to objects but are written with EF sql in mind. Some discrepancies
    /// can occur due to specific behavior differences of those methods of execution.
    /// This is an attempt to mitigate the impact of those differences.
    /// </summary>
    public static class QueryRewriter
    {
        /// <summary>
        /// Gets a list of factory methods that will be used to crate visitors
        /// that will modify expression to be better suited for Linq to objects execution.
        /// </summary>
        public static List<Func<ExpressionVisitor>> Visitors { get; } = new List<Func<ExpressionVisitor>>
        {
           () => new DefaultRewriter(),
        };

        /// <summary>
        /// Rewrites expression by execution all visitors.
        /// </summary>
        /// <param name="expression">Expression to rewrite.</param>
        /// <returns>Rewritten expression.</returns>
        public static Expression Rewrite(Expression expression)
        {
            foreach (var expressionVisitor in Visitors)
            {
                expression = expressionVisitor().Visit(expression);
            }

            return expression;
        }
    }
}