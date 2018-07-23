// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNet.OData.Common;
using Microsoft.AspNet.OData.Formatter;
using Microsoft.AspNet.OData.Formatter.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Microsoft.AspNet.OData.Query.Expressions
{
    /// <summary>
    /// Applies the given <see cref="SelectExpandQueryOption"/> to the given <see cref="IQueryable"/>.
    /// </summary>
    internal partial class SelectExpandBinder
    {

        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification =
            "Class coupling acceptable")]
        private Expression BuildPropertyContainer(IEdmEntityType elementType, Expression source,
            Dictionary<IEdmNavigationProperty, ExpandedNavigationSelectItem> propertiesToExpand,
            ISet<IEdmStructuralProperty> propertiesToInclude, ISet<IEdmStructuralProperty> autoSelectedProperties,
            SelectExpandClause selectExpandClause)
        {
            IList<NamedPropertyExpression> includedProperties = new List<NamedPropertyExpression>();

            foreach (KeyValuePair<IEdmNavigationProperty, ExpandedNavigationSelectItem> kvp in propertiesToExpand)
            {
                IEdmNavigationProperty propertyToExpand = kvp.Key;
                ExpandedNavigationSelectItem expandItem = kvp.Value;
                SelectExpandClause projection = expandItem.SelectAndExpand;

                ModelBoundQuerySettings querySettings = EdmLibHelpers.GetModelBoundQuerySettings(propertyToExpand,
                    propertyToExpand.ToEntityType(),
                    _context.Model);

                Expression propertyName = CreatePropertyNameExpression(elementType, propertyToExpand, source);
                Expression propertyValue = CreatePropertyValueExpressionWithFilter(elementType, propertyToExpand,
                    source,
                    expandItem.FilterOption);
                Expression nullCheck = GetNullCheckExpression(propertyToExpand, propertyValue, projection);

                Expression countExpression = CreateTotalCountExpression(propertyValue, expandItem);

                // projection can be null if the expanded navigation property is not further projected or expanded.
                if (projection != null)
                {
                    int? modelBoundPageSize = querySettings == null ? null : querySettings.PageSize;
                    propertyValue = ProjectAsWrapper(propertyValue, projection, propertyToExpand.ToEntityType(),
                        expandItem.NavigationSource, expandItem, modelBoundPageSize);
                }

                NamedPropertyExpression propertyExpression = new NamedPropertyExpression(propertyName, propertyValue);
                if (projection != null)
                {
                    if (!propertyToExpand.Type.IsCollection())
                    {
                        propertyExpression.NullCheck = nullCheck;
                    }
                    else if (_settings.PageSize.HasValue)
                    {
                        propertyExpression.PageSize = _settings.PageSize.Value;
                    }
                    else
                    {
                        if (querySettings != null && querySettings.PageSize.HasValue)
                        {
                            propertyExpression.PageSize = querySettings.PageSize.Value;
                        }
                    }

                    propertyExpression.TotalCount = countExpression;
                    propertyExpression.CountOption = expandItem.CountOption;
                }

                includedProperties.Add(propertyExpression);
            }

            foreach (IEdmStructuralProperty propertyToInclude in propertiesToInclude)
            {
                Expression propertyName = CreatePropertyNameExpression(elementType, propertyToInclude, source);
                Expression propertyValue = CreatePropertyValueExpression(elementType, propertyToInclude, source);
                includedProperties.Add(new NamedPropertyExpression(propertyName, propertyValue));
            }

            foreach (IEdmStructuralProperty propertyToInclude in autoSelectedProperties)
            {
                Expression propertyName = CreatePropertyNameExpression(elementType, propertyToInclude, source);
                Expression propertyValue = CreatePropertyValueExpression(elementType, propertyToInclude, source);
                includedProperties.Add(new NamedPropertyExpression(propertyName, propertyValue) {AutoSelected = true});
            }

            var isSelectingOpenTypeSegments = GetSelectsOpenTypeSegments(selectExpandClause, elementType);
            var openTypeSegments = GetOpenTypeSegmentsOfSelect(selectExpandClause, elementType).ToList();

            var efPropertyMethod = typeof(EF).GetMethod("Property").MakeGenericMethod(typeof(object));
            foreach (var openTypeSegment in openTypeSegments)
            {
                var propertyName = Expression.Constant(openTypeSegment.SelectedPath.LastSegment.Identifier);
                var propertyValue = Expression.Call(efPropertyMethod, source, propertyName);
                includedProperties.Add(new NamedPropertyExpression(propertyName, propertyValue));
            }
            
            /*
            if (isSelectingOpenTypeSegments)
            {
                var dynamicPropertyDictionary = EdmLibHelpers.GetDynamicPropertyDictionary(elementType, _model);

                Expression propertyName = Expression.Constant(dynamicPropertyDictionary.Name);
                Expression propertyValue = Expression.Property(source, dynamicPropertyDictionary.Name);
                Expression nullablePropertyValue = ExpressionHelpers.ToNullable(propertyValue);
                if (_settings.HandleNullPropagation == HandleNullPropagationOption.True)
                {
                    // source == null ? null : propertyValue
                    propertyValue = Expression.Condition(
                        test: Expression.Equal(source, Expression.Constant(value: null)),
                        ifTrue: Expression.Constant(value: null, type: TypeHelper.ToNullable(propertyValue.Type)),
                        ifFalse: nullablePropertyValue);
                }
                else
                {
                    propertyValue = nullablePropertyValue;
                }

                includedProperties.Add(new NamedPropertyExpression(propertyName, propertyValue));
            }
            */

            // create a property container that holds all these property names and values.
            return PropertyContainer.CreatePropertyContainer(includedProperties);
        }
        

        private static IEnumerable<PathSelectItem> GetOpenTypeSegmentsOfSelect(SelectExpandClause selectExpandClause, IEdmEntityType entityType)
        {
            if (!entityType.IsOpen)
            {
                return Enumerable.Empty<PathSelectItem>();
            }

            if (selectExpandClause.AllSelected)
            {
                return Enumerable.Empty<PathSelectItem>();
            }

            return selectExpandClause.SelectedItems.OfType<PathSelectItem>().Where(x => x.SelectedPath.LastSegment is DynamicPathSegment);
        }
    }
}