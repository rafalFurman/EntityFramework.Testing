//-----------------------------------------------------------------------------------------------------
// <copyright file="DefaultRewriter.cs" company="QDARC Rafał Furman">
// Copyright (c) Rafał Furman. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------------------------------------

namespace EntityFramework.Testing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;

    /// <summary>
    /// Replaces call to <see cref="Enumerable.DefaultIfEmpty{TSource}(System.Collections.Generic.IEnumerable{TSource})" />
    /// with <see cref="Enumerable.DefaultIfEmpty{TSource}(System.Collections.Generic.IEnumerable{TSource}, TSource)"/>
    /// and <see cref="Queryable.DefaultIfEmpty{TSource}(System.Linq.IQueryable{TSource})" />
    /// with <see cref="Queryable.DefaultIfEmpty{TSource}(System.Linq.IQueryable{TSource}, TSource)"/>
    /// and <see cref="Enumerable.FirstOrDefault{TSource}(System.Collections.Generic.IEnumerable{TSource})" />
    /// with <see cref="FirstOrDefault{TSource}(System.Collections.Generic.IEnumerable{TSource},TSource)" />
    /// and <see cref="Enumerable.FirstOrDefault{TSource}(System.Collections.Generic.IEnumerable{TSource}, System.Func{TSource,bool})" />
    /// with <see cref="FirstOrDefault{TSource}(System.Collections.Generic.IEnumerable{TSource}, System.Func{TSource,bool}, TSource)" />
    /// also wrapps coalesce operator with another providing default value if right operand is not constant already.
    /// This rewriter dose its work only if public parametless constructor is available for TSource type.
    /// </summary>
    public class DefaultRewriter : ExpressionVisitor
    {
        private static readonly MethodInfo EnumerableDefaultIfEmpty
            = typeof(Enumerable).GetMethods()
                .Single(x => x.Name == "DefaultIfEmpty" && x.GetParameters().Length == 2);

        private static readonly MethodInfo QueryableDefaultIfEmpty
            = typeof(Queryable).GetMethods()
                .Single(x => x.Name == "DefaultIfEmpty" && x.GetParameters().Length == 2);

        /// <summary>
        /// Gets first or default value with option of providing the default.
        /// </summary>
        /// <typeparam name="TSource">Type of items.</typeparam>
        /// <param name="source">Items list.</param>
        /// <param name="defaultValue">Default item if none present in list.</param>
        /// <returns>First or default item.</returns>
        public static TSource FirstOrDefault<TSource>(IEnumerable<TSource> source, TSource defaultValue)
        {
            return source.Concat(new[] { defaultValue }).FirstOrDefault();
        }

        /// <summary>
        /// Gets first or default value that conforms to filter with option of providing the default value.
        /// Default value is not verified by filter.
        /// </summary>
        /// <typeparam name="TSource">Type of items.</typeparam>
        /// <param name="source">Items list.</param>
        /// <param name="filter">Additional filter.</param>
        /// <param name="defaultValue">Default item if none present in list.</param>
        /// <returns>First or default item.</returns>
        public static TSource FirstOrDefault<TSource>(IEnumerable<TSource> source, Func<TSource, bool> filter, TSource defaultValue)
        {
            return source.Where(filter).Concat(new[] { defaultValue }).FirstOrDefault();
        }

        /// <summary>
        /// Visits the children of the System.Linq.Expressions.MethodCallExpression.
        /// </summary>
        /// <param name="node">The expression to visit.</param>
        /// <returns>
        /// The modified expression, if it or any subexpression was modified; otherwise,
        /// returns the original expression.
        /// </returns>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "DefaultIfEmpty"
                && node.Method.GetParameters().Length == 1)
            {
                return this.HandleDefaultIfEmpty(node);
            }

            if (node.Method.Name == "FirstOrDefault" && node.Method.DeclaringType == typeof(Enumerable))
            {
                return this.HandeFirstOrDefault(node);
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>
        /// Visits the children of the System.Linq.Expressions.BinaryExpression.
        /// </summary>
        /// <param name="node">The expression to visit.</param>
        /// <returns>
        /// The modified expression, if it or any subexpression was modified; otherwise,
        /// returns the original expression.
        /// </returns>
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.Coalesce)
            {
                return this.HandleCoalesce(node);
            }

            if (node.NodeType == ExpressionType.Equal && this.IsNullable(node.Left.Type))
            {
                return this.HandleEqual(node);
            }

            if (node.NodeType == ExpressionType.NotEqual && this.IsNullable(node.Left.Type))
            {
                return this.HandelNotEqual(node);
            }

            return base.VisitBinary(node);
        }

        private Expression HandelNotEqual(BinaryExpression node)
        {
            if (node.Left.NodeType == ExpressionType.Constant)
            {
                var constant = (ConstantExpression)node.Left;
                if (constant.Value == null)
                {
                    return Expression.NotEqual(
                        Expression.Constant(this.GetValue(node), node.Left.Type),
                        this.Visit(node.Right));
                }
            }

            if (node.Right.NodeType == ExpressionType.Constant)
            {
                var constant = (ConstantExpression)node.Right;
                if (constant.Value == null)
                {
                    return Expression.NotEqual(
                        this.Visit(node.Left),
                        Expression.Constant(this.GetValue(node), node.Left.Type));
                }
            }

            return base.VisitBinary(node);
        }

        private Expression HandleEqual(BinaryExpression node)
        {
            if (node.Left.NodeType == ExpressionType.Constant)
            {
                var constant = (ConstantExpression)node.Left;
                if (constant.Value == null)
                {
                    return Expression.Equal(
                        Expression.Constant(this.GetValue(node), node.Left.Type),
                        this.Visit(node.Right));
                }
            }

            if (node.Right.NodeType == ExpressionType.Constant)
            {
                var constant = (ConstantExpression)node.Right;
                if (constant.Value == null)
                {
                    return Expression.Equal(
                        this.Visit(node.Left),
                        Expression.Constant(this.GetValue(node), node.Left.Type));
                }
            }

            return base.VisitBinary(node);
        }

        private Expression HandleCoalesce(BinaryExpression node)
        {
            if (node.Right.NodeType == ExpressionType.Constant)
            {
                return base.VisitBinary(node);
            }

            var sourceType = node.Type;
            if (sourceType.GetConstructor(new Type[0]) == null)
            {
                return base.VisitBinary(node);
            }

            var defaultValue = Activator.CreateInstance(sourceType);
            return Expression.Coalesce(base.VisitBinary(node), Expression.Constant(defaultValue));
        }

        private object GetValue(BinaryExpression node)
        {
            var ctor = node.Left.Type.GetConstructors().Single(x => x.GetParameters().Length != 0);
            return ctor.Invoke(new object[] { Activator.CreateInstance(node.Left.Type.GetGenericArguments()[0]) });
        }

        private bool IsNullable(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        private Expression HandeFirstOrDefault(MethodCallExpression node)
        {
            var sourceType = node.Method.GetGenericArguments().Single();
            if (sourceType.GetConstructor(new Type[0]) == null)
            {
                return base.VisitMethodCall(node);
            }

            MethodInfo method;

            if (node.Arguments.Count == 1)
            {
                method = typeof(DefaultRewriter).GetMethods()
                    .Single(x => x.Name == "FirstOrDefault" && x.GetParameters().Length == 2);
            }
            else
            {
                method = typeof(DefaultRewriter).GetMethods()
                    .Single(x => x.Name == "FirstOrDefault" && x.GetParameters().Length == 3);
            }

            method = method.MakeGenericMethod(sourceType);

            var defaultValue = Activator.CreateInstance(sourceType);
            return Expression.Call(
                method,
                node.Arguments.Select(this.Visit).Concat(
                    new Expression[]
                    {
                        Expression.Constant(defaultValue),
                    }).ToArray());
        }

        private Expression HandleDefaultIfEmpty(MethodCallExpression node)
        {
            var sourceType = node.Method.GetGenericArguments().Single();
            if (sourceType.GetConstructor(new Type[0]) == null)
            {
                return base.VisitMethodCall(node);
            }

            MethodInfo overload;
            if (node.Method.DeclaringType == typeof(Enumerable))
            {
                overload = EnumerableDefaultIfEmpty
                    .MakeGenericMethod(node.Method.GetGenericArguments());
            }
            else if (node.Method.DeclaringType == typeof(Queryable))
            {
                overload = QueryableDefaultIfEmpty
                    .MakeGenericMethod(node.Method.GetGenericArguments());
            }
            else
            {
                return base.VisitMethodCall(node);
            }

            var defaultValue = Activator.CreateInstance(sourceType);
            return Expression.Call(overload, this.Visit(node.Arguments.Single()), Expression.Constant(defaultValue));
        }
    }
}