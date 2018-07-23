using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.AspNet.OData.Formatter;
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.UriParser;

namespace Microsoft.AspNet.OData.Query.Expressions
{
    /// <summary>
    /// Translates an OData $filter parse tree represented by <see cref="FilterClause"/> to
    /// an <see cref="Expression"/> and applies it to an <see cref="IQueryable"/>.
    /// </summary>
    public partial class FilterBinder
    {
        /// <summary>
        /// Binds a <see cref="SingleValueOpenPropertyAccessNode"/> to create a LINQ <see cref="Expression"/> that
        /// represents the semantics of the <see cref="SingleValueOpenPropertyAccessNode"/>.
        /// </summary>
        /// <param name="openNode">The node to bind.</param>
        /// <returns>The LINQ <see cref="Expression"/> created.</returns>
        public virtual Expression BindDynamicPropertyAccessQueryNode(SingleValueOpenPropertyAccessNode openNode)
        {
            if (EdmLibHelpers.IsDynamicTypeWrapper(_filterType))
            {
                return GetFlattenedPropertyExpression(openNode.Name) ?? Expression.Property(Bind(openNode.Source), openNode.Name);
            }
            PropertyInfo prop = GetDynamicPropertyContainer(openNode);
            
            var efPropertyMethod = typeof(EF).GetMethod("Property").MakeGenericMethod(typeof(object));
            var propertyAccessExpression = Expression.Call(efPropertyMethod, Bind(openNode.Source), Expression.Constant(openNode.Name));
            return propertyAccessExpression;
        }
    }
}
