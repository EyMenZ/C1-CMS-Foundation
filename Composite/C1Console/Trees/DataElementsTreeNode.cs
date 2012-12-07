﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Composite.C1Console.Elements;
using Composite.C1Console.Security;
using Composite.C1Console.Users;
using Composite.C1Console.Workflow;
using Composite.Core.Extensions;
using Composite.Core.Linq;
using Composite.Core.ResourceSystem;
using Composite.Core.Types;
using Composite.Data;
using Composite.Data.ProcessControlled;


namespace Composite.C1Console.Trees
{
    /// <summary>    
    /// </summary>
    /// <exclude />
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)] 
    public class DataElementsTreeNode : DataFilteringTreeNode
    {
        /// <exclude />
        public Type InterfaceType { get; internal set; }        // Requried        

        /// <exclude />
        public string Label { get; internal set; }              // Optional

        /// <exclude />
        public string ToolTip { get; internal set; }            // Optional

        /// <exclude />
        public ResourceHandle Icon { get; internal set; }       // Defaults to C1 standard data icons

        /// <exclude />
        public ResourceHandle OpenedIcon { get; internal set; } // Defaults to C1 standard data icons or Icon if it is setted

        /// <exclude />
        public LeafDisplayMode Display { get; internal set; }   // Optional

        /// <exclude />
        public bool ShowForeignItems { get; internal set; }     // Optional


        // Cached values
        private Dictionary<Type, ParentFilterHelper> ParentFilteringHelpers { get; set; }

        // Cached values
        private PropertyInfo KeyPropertyInfo { get; set; }

        // Cached values
        private ParentIdFilterNode JoinParentIdFilterNode { get; set; }
        private DataElementsTreeNode JoinDataElementsTreeNode { get; set; }
        private PropertyInfo JoinInnerKeyReferencePropertyInfo { get; set; }

        private DynamicValuesHelper LabelDynamicValuesHelper { get; set; }
        private DynamicValuesHelper ToolTipDynamicValuesHelper { get; set; }


        private static readonly ResourceHandle LocalizeDataTypeIcon = ResourceHandle.BuildIconFromDefaultProvider("tree-localize-data");
        private static readonly PermissionType[] LocalizeDataPermissionTypes = new PermissionType[] { PermissionType.Add };


        /// <exclude />
        public override IEnumerable<EntityToken> GetEntityTokens(EntityToken childEntityToken, TreeNodeDynamicContext dynamicContext)
        {
            IEnumerable<IData> dataset = GetDataset(dynamicContext, false).DataItems;

            return dataset.Select(f => (EntityToken)f.GetDataEntityToken()).Evaluate();
        }



        /// <exclude />
        public override AncestorResult GetParentEntityToken(EntityToken ownEntityToken, Type parentInterfaceOfInterest, TreeNodeDynamicContext dynamicContext)
        {
            if (this.ParentFilteringHelpers == null)
            {
                throw new InvalidOperationException(string.Format("Failed to find parent, are you missing a parent filter for the type '{0}'", this.InterfaceType));
            }

            ParentFilterHelper helper;
            if (this.ParentFilteringHelpers.TryGetValue(parentInterfaceOfInterest, out helper) == false)
            {
                // We cant find the interface of interest directly, so we will give 'some' parent entity token
                // by using 'one' of our own parent id filters

                helper = this.ParentFilteringHelpers.First().Value;
            }

            DataEntityToken dataEntityToken = (DataEntityToken)ownEntityToken;
            IData data = dataEntityToken.Data;

            Verify.ArgumentCondition(data != null, "ownEntityToken", "Failed to get data");

            object parentFieldValue = helper.ParentReferencedPropertyInfo.GetValue(data, null);

            ParameterExpression parameterExpression = Expression.Parameter(helper.ParentIdFilterNode.ParentFilterType, "data");

            Expression expression = Expression.Equal(
                ExpressionHelper.CreatePropertyExpression(helper.ParentRefereePropertyName, parameterExpression),
                Expression.Constant(parentFieldValue, helper.ParentReferencedPropertyInfo.PropertyType)
            );

            Expression whereExpression = ExpressionHelper.CreateWhereExpression(
                DataFacade.GetData(helper.ParentIdFilterNode.ParentFilterType).Expression,
                parameterExpression, expression
            );

            IData parentDataItem = ExpressionHelper.GetCastedObjects<IData>(helper.ParentIdFilterNode.ParentFilterType, whereExpression)
                                   .FirstOrDefault();

            Verify.IsNotNull(parentDataItem, "Failed to get parent data item. Check if there's a broken parent reference.");
            
            DataEntityToken parentEntityToken = parentDataItem.GetDataEntityToken();

            TreeNode parentTreeNode = this.ParentNode;
            while (((parentTreeNode as DataElementsTreeNode) == null) ||
                   ((parentTreeNode as DataElementsTreeNode).InterfaceType != parentEntityToken.InterfaceType))
            {
                parentTreeNode = parentTreeNode.ParentNode;
            }

            return new AncestorResult(parentTreeNode, parentEntityToken);
        }



        /// <exclude />
        protected override IEnumerable<Element> OnGetElements(EntityToken parentEntityToken, TreeNodeDynamicContext dynamicContext)
        {
            IEnumerable<IData> dataItems;
            IEnumerable<object> keys;
            List<object> itemKeys = new List<object>();

            NodeDataSet dataset = GetDataset(dynamicContext);

            bool localizationEndabled = this.ShowForeignItems && !UserSettings.ActiveLocaleCultureInfo.Equals(UserSettings.ForeignLocaleCultureInfo);

            keys = dataset.JoinedKeys;
            if (localizationEndabled && UserSettings.ForeignLocaleCultureInfo != null)
            {
                NodeDataSet foreignDataset;
                using (new DataScope(UserSettings.ForeignLocaleCultureInfo))
                {
                    foreignDataset = GetDataset(dynamicContext);
                }

                ParameterExpression parameterExpression = Expression.Parameter(this.InterfaceType, "data");

                IEnumerable combinedData = dataset.DataItems.Concat(foreignDataset.DataItems).ToCastedDataEnumerable(this.InterfaceType);

                Expression orderByExpression = this.CreateAccumulatedOrderByExpression(combinedData.AsQueryable().Expression, parameterExpression);

                dataItems = combinedData.AsQueryable().Provider.CreateQuery<IData>(orderByExpression);

                foreach (IData data in dataset.DataItems)
                {
                    Verify.IsNotNull(data, "Fetching data for data interface '{0}' with expression '{1}' yielded an null element".FormatWith(this.InterfaceType, orderByExpression));

                    object keyValue = this.KeyPropertyInfo.GetValue(data, null);
                    itemKeys.Add(keyValue);
                }

                keys = keys.ConcatOrDefault(foreignDataset.JoinedKeys);
            }
            else
            {
                dataItems = dataset.DataItems;
                itemKeys = new List<object>();
            }


            DynamicValuesHelperReplaceContext replaceContext = new DynamicValuesHelperReplaceContext
            {
                PiggybagDataFinder = new PiggybagDataFinder(dynamicContext.Piggybag, parentEntityToken)
            };


            List<Element> elements = new List<Element>();

            foreach (IData data in dataItems)
            {
                Verify.IsNotNull(data, "Fetching data for data interface '{0}' yielded an null element".FormatWith(this.InterfaceType));

                replaceContext.CurrentDataItem = data;
                

                object keyValue = this.KeyPropertyInfo.GetValue(data, null);

                bool itemLocalizationEnabledAndForeign = localizationEndabled && !data.DataSourceId.LocaleScope.Equals(UserSettings.ActiveLocaleCultureInfo);

                if (itemLocalizationEnabledAndForeign && itemKeys.Contains(keyValue)) continue;

                Element element = new Element(new ElementHandle
                (
                    dynamicContext.ElementProviderName,
                    data.GetDataEntityToken(),
                    dynamicContext.Piggybag.PreparePiggybag(this.ParentNode, parentEntityToken)
                ));


                bool hasChildren;
                bool isDisabled = false;
                ResourceHandle icon;
                ResourceHandle openedIcon;
                if (itemLocalizationEnabledAndForeign)
                {
                    hasChildren = false;                    
                    isDisabled = data.IsLocaleDisabled();

                    if (this.Icon != null)
                    {
                        icon = this.Icon;
                        openedIcon = this.OpenedIcon;
                    }
                    else
                    {
                        icon = data.GetForeignIcon();
                        openedIcon = icon;
                    }
                 }
                else
                {
                    if (this.Display != LeafDisplayMode.Auto)
                    {
                        hasChildren = ChildNodes.Any();
                    }
                    else
                    {
                        hasChildren = ChildNodes.Any(childNode => childNode is SimpleElementTreeNode)
                                      || (keys != null && keys.Contains(keyValue));
                    }

                    if (this.Icon != null)
                    {
                        icon = this.Icon;
                        openedIcon = this.OpenedIcon;
                    }
                    else
                    {
                        icon = icon = data.GetIcon();
                        openedIcon = icon;
                    }                    
                }

                string label = this.Label.IsNullOrEmpty() 
                                ? data.GetLabel() 
                                : this.LabelDynamicValuesHelper.ReplaceValues(replaceContext);                

                string toolTip = this.ToolTip.IsNullOrEmpty() 
                                ? label 
                                : this.ToolTipDynamicValuesHelper.ReplaceValues(replaceContext);

                if (itemLocalizationEnabledAndForeign)
                {
                    label = string.Format("{0} ({1})", label, DataLocalizationFacade.GetCultureTitle(UserSettings.ForeignLocaleCultureInfo));

                    if (data.IsLocaleDisabled())
                    {
                        toolTip = StringResourceSystemFacade.GetString("Composite.C1Console.Trees", "LocalizeDataWorkflow.DisabledData");
                    }
                    else
                    {
                        toolTip = string.Format("{0} ({1})", toolTip, DataLocalizationFacade.GetCultureTitle(UserSettings.ForeignLocaleCultureInfo));
                    }
                }

                element.VisualData = new ElementVisualizedData
                {
                    Label = label,
                    ToolTip = toolTip,
                    HasChildren = hasChildren,
                    Icon = icon,
                    OpenedIcon = openedIcon,
                    IsDisabled = isDisabled
                };


                if (itemLocalizationEnabledAndForeign == true)
                {
                    WorkflowActionToken actionToken = new WorkflowActionToken(
                        WorkflowFacade.GetWorkflowType("Composite.C1Console.Trees.Workflows.LocalizeDataWorkflow"),
                        LocalizeDataPermissionTypes);

                    element.AddAction(new ElementAction(new ActionHandle(actionToken))
                    {
                        VisualData = new ActionVisualizedData
                        {
                            Label = StringResourceSystemFacade.GetString("Composite.C1Console.Trees", "LocalizeDataWorkflow.LocalizeDataLabel"),
                            ToolTip = StringResourceSystemFacade.GetString("Composite.C1Console.Trees", "LocalizeDataWorkflow.LocalizeDataToolTip"),
                            Icon = LocalizeDataTypeIcon,
                            Disabled = false,
                            ActionLocation = ActionLocation.OtherPrimaryActionLocation
                        }
                    });
                }

                elements.Add(element);
            }

            if (this.OrderByNodes.Any() == false)
            {
                return elements.OrderBy(f => f.VisualData.Label);
            }
            else
            {
                return elements;
            }
        }



        /// <exclude />
        protected override void OnInitialize()
        {
            if (!typeof(IData).IsAssignableFrom(this.InterfaceType))
            {
                AddValidationError("TreeValidationError.Common.NotImplementingIData", this.InterfaceType, typeof(IData));
                return;
            }

            IEnumerable<Type> siblingInterfaceTypes = this.ParentNode.ChildNodes.Where(f => f.GetType() == typeof(DataElementsTreeNode)).Select(f => ((DataElementsTreeNode)f).InterfaceType).ToList();
            if (siblingInterfaceTypes.Count() != siblingInterfaceTypes.Distinct().Count())
            {
                AddValidationError("TreeValidationError.DataElementsTreeNode.SameInterfaceUsedTwice", this.InterfaceType);
                return;
            }


            this.KeyPropertyInfo = this.CurrentDataInterfaceType.GetKeyProperties()[0];


            foreach (ParentIdFilterNode parentIdFilterNode in this.FilterNodes.OfType<ParentIdFilterNode>())
            {
                if (this.ParentFilteringHelpers == null) this.ParentFilteringHelpers = new Dictionary<Type, ParentFilterHelper>();

                if (this.ParentFilteringHelpers.ContainsKey(parentIdFilterNode.ParentFilterType) == true)
                {
                    AddValidationError("TreeValidationError.DataElementsTreeNode.SameParentFilterInterfaceUsedTwice", parentIdFilterNode.ParentFilterType);
                    return;
                }

                ParentFilterHelper helper = new ParentFilterHelper();

                helper.ParentIdFilterNode = parentIdFilterNode;
                helper.ParentReferencedPropertyInfo = this.InterfaceType.GetPropertiesRecursively().Single(f => f.Name == parentIdFilterNode.ReferenceFieldName);
                helper.ParentRefereePropertyName = parentIdFilterNode.ParentFilterType.GetKeyProperties()[0].Name;

                this.ParentFilteringHelpers.Add(parentIdFilterNode.ParentFilterType, helper);
            }



            this.JoinParentIdFilterNode = null;
            this.JoinDataElementsTreeNode = null;
            foreach (TreeNode decendantTreeNode in this.DescendantsBreadthFirst())
            {
                DataElementsTreeNode dataElementTreeNode = decendantTreeNode as DataElementsTreeNode;
                if (dataElementTreeNode == null) continue;

                ParentIdFilterNode parentIdFilterNode = dataElementTreeNode.FilterNodes.OfType<ParentIdFilterNode>()
                                                        .FirstOrDefault(f => f.ParentFilterType == this.InterfaceType);

                if (parentIdFilterNode != null)
                {
                    if (this.JoinParentIdFilterNode == null)
                    {
                        this.JoinParentIdFilterNode = parentIdFilterNode;
                        this.JoinDataElementsTreeNode = dataElementTreeNode;
                        this.JoinInnerKeyReferencePropertyInfo = this.JoinDataElementsTreeNode.CurrentDataInterfaceType.GetAllProperties().Single(f => f.Name == this.JoinParentIdFilterNode.ReferenceFieldName);
                    }
                    else if (this.Display != LeafDisplayMode.Lazy)
                    {
                        AddValidationError("TreeValidationError.DataElementsTreeNode.MoreThanOnParentFilterIsPointingToMe", this.InterfaceType);
                        return;
                    }
                }
            }

            if (this.Label != null)
            {
                this.LabelDynamicValuesHelper = new DynamicValuesHelper(this.Label);
                this.LabelDynamicValuesHelper.Initialize(this);
            }

            if (this.ToolTip != null)
            {
                this.ToolTipDynamicValuesHelper = new DynamicValuesHelper(this.ToolTip);
                this.ToolTipDynamicValuesHelper.Initialize(this);
            }

            if (!typeof(ILocalizedControlled).IsAssignableFrom(this.InterfaceType))
            {
                this.ShowForeignItems = false;
            }
        }



        internal override Type CurrentDataInterfaceType
        {
            get { return this.InterfaceType; }
        }


        /// <summary>
        /// Data related to a tree node
        /// </summary>
        private class NodeDataSet
        {
            public IEnumerable<IData> DataItems;
            public IEnumerable<object> JoinedKeys;
        }


        private NodeDataSet GetDataset(TreeNodeDynamicContext dynamicContext, bool returnJoinedTableKeys = true)
        {
            List<object> innerKeys = null;

            Expression expression;

            if ((this.Display == LeafDisplayMode.Compact) && (this.JoinDataElementsTreeNode != null))
            {
                expression = CreateJoinExpression(dynamicContext);
            }
            else if ((this.Display == LeafDisplayMode.Auto) && (this.JoinDataElementsTreeNode != null))
            {
                expression = CreateSimpleExpression(dynamicContext);

                if (returnJoinedTableKeys)
                {
                    //MRJ: Multible Parent Filter: Not a real problem here as we just make a request for every filter
                    Expression innerExpression = CreateInnerExpression(dynamicContext, this.JoinParentIdFilterNode, this.JoinDataElementsTreeNode, false);

                    if (dynamicContext.Direction == TreeNodeDynamicContextDirection.Down)
                    {
                        innerExpression.DebugLogExpression("DataElementTreeNode", label: "Parent ids with children expression:");
                    }

                    innerKeys = DataFacade.GetData(this.JoinDataElementsTreeNode.CurrentDataInterfaceType).Provider
                        .CreateQuery(innerExpression)
                        .ToEnumerableOfObjects()
                        .ToList();
                }
            }
            else
            {
                expression = CreateSimpleExpression(dynamicContext);
            }

            if (dynamicContext.Direction == TreeNodeDynamicContextDirection.Down) expression.DebugLogExpression("DataElementTreeNode");

            return new NodeDataSet
                       {
                           DataItems = ExpressionHelper.GetCastedObjects<IData>(this.InterfaceType, expression),
                           JoinedKeys = innerKeys
                       };
        }



        private Expression CreateSimpleExpression(TreeNodeDynamicContext dynamicContext)
        {
            ParameterExpression parameterExpression = Expression.Parameter(this.InterfaceType, "data");

            Expression filterExpression = CreateAccumulatedFilterExpression(parameterExpression, this.InterfaceType, dynamicContext);

            Expression whereExpression = ExpressionHelper.CreateWhereExpression(DataFacade.GetData(this.InterfaceType).Expression, parameterExpression, filterExpression);

            Expression resultExpression = whereExpression;
            bool isFirst = true;
            foreach (OrderByNode orderByNode in this.OrderByNodes)
            {
                resultExpression = orderByNode.CreateOrderByExpression(resultExpression, parameterExpression, isFirst);
                isFirst = false;
            }

            return resultExpression;
        }



        private Expression CreateJoinExpression(TreeNodeDynamicContext dynamicContext)
        {
            //MRJ: Multible Parent Filter: Here we have to make either multiple calls or create a multiple inner join, kinda hard
            // Create inner expression tree
            Expression innerDistinctExpression = CreateInnerExpression(dynamicContext, this.JoinParentIdFilterNode, this.JoinDataElementsTreeNode);


            // Create outer expression tree
            ParameterExpression outerParameterExpression = Expression.Parameter(this.InterfaceType, "outerData");

            Expression outerFilterExpression = CreateAccumulatedFilterExpression(outerParameterExpression, this.InterfaceType, dynamicContext);

            Expression outerWhereExpression = ExpressionHelper.CreateWhereExpression(DataFacade.GetData(this.InterfaceType).Expression, outerParameterExpression, outerFilterExpression);

            Expression outerResultExpression = outerWhereExpression;
            bool isFirst = true;
            foreach (OrderByNode orderByNode in this.OrderByNodes)
            {
                outerResultExpression = orderByNode.CreateOrderByExpression(outerResultExpression, outerParameterExpression, isFirst);
                isFirst = false;
            }


            // Create join lambda expressions            
            PropertyInfo outerKeyPropertyInfo = this.KeyPropertyInfo;
            LambdaExpression outerKeySelectorExpression = Expression.Lambda(Expression.Property(outerParameterExpression, outerKeyPropertyInfo), outerParameterExpression);

            ParameterExpression innerKeySelectorParameterExpression = Expression.Parameter(this.JoinInnerKeyReferencePropertyInfo.PropertyType, "innerKeyParam");
            LambdaExpression innerKeySelectorExpression = Expression.Lambda(innerKeySelectorParameterExpression, innerKeySelectorParameterExpression);

            ParameterExpression parameterExpression1 = Expression.Parameter(this.InterfaceType, "param1");
            ParameterExpression parameterExpression2 = Expression.Parameter(outerKeyPropertyInfo.PropertyType, "param2");
            LambdaExpression resultSelector = Expression.Lambda(parameterExpression1, parameterExpression1, parameterExpression2);

            Expression joinExpression = ExpressionHelper.CreateJoinExpression(outerResultExpression, innerDistinctExpression, outerKeySelectorExpression, innerKeySelectorExpression, resultSelector);

            return joinExpression;
        }



        private static Expression CreateInnerExpression(TreeNodeDynamicContext dynamicContext, ParentIdFilterNode parentIdFilterNode, DataElementsTreeNode dataElementsTreeNode, bool includeJoin = true)
        {
            Type interfaceType = dataElementsTreeNode.CurrentDataInterfaceType;

            ParameterExpression parameterExpression = Expression.Parameter(interfaceType, "innerData");

            List<int> filtersToSkip = new List<int>() { parentIdFilterNode.Id };
            foreach (ParentIdFilterNode childParentIdFilterNode in dataElementsTreeNode.FilterNodes.OfType<ParentIdFilterNode>().Where(f => f.Id != parentIdFilterNode.Id))
            {
                if (childParentIdFilterNode.ParentFilterTypeTreeNode.IsAncestor(parentIdFilterNode.ParentFilterTypeTreeNode) == true)
                {
                    filtersToSkip.Add(childParentIdFilterNode.Id);
                }
            }

            Expression filterExpression = dataElementsTreeNode.CreateAccumulatedFilterExpression(parameterExpression, interfaceType, dynamicContext, filtersToSkip);

            Expression whereExpression = ExpressionHelper.CreateWhereExpression(DataFacade.GetData(interfaceType).Expression, parameterExpression, filterExpression);


            if ((includeJoin == true) && (dataElementsTreeNode.JoinDataElementsTreeNode != null))
            {
                //MRJ: Multiple Parent Filter: Recursive call, so we would have to make multiple recursive calls
                Expression innerInnerExpression = CreateInnerExpression(dynamicContext, dataElementsTreeNode.JoinParentIdFilterNode, dataElementsTreeNode.JoinDataElementsTreeNode);

                // Create join lambda expressions
                PropertyInfo outerKeyPropertyInfo = dataElementsTreeNode.KeyPropertyInfo;
                LambdaExpression outerKeySelectorExpression = Expression.Lambda(Expression.Property(parameterExpression, outerKeyPropertyInfo), parameterExpression);

                Type innerKeyType = TypeHelpers.FindElementType(innerInnerExpression);
                ParameterExpression innerKeySelectorParameterExpression = Expression.Parameter(innerKeyType, "innerKeyParam");
                LambdaExpression innerKeySelectorExpression = Expression.Lambda(innerKeySelectorParameterExpression, innerKeySelectorParameterExpression);

                ParameterExpression parameterExpression1 = Expression.Parameter(dataElementsTreeNode.CurrentDataInterfaceType, "param1");
                ParameterExpression parameterExpression2 = Expression.Parameter(outerKeyPropertyInfo.PropertyType, "param2");
                LambdaExpression resultSelector = Expression.Lambda(parameterExpression1, parameterExpression1, parameterExpression2);

                Expression joinExpression = ExpressionHelper.CreateJoinExpression(
                    whereExpression,
                    innerInnerExpression,
                    outerKeySelectorExpression,
                    innerKeySelectorExpression,
                    resultSelector);

                whereExpression = joinExpression;
            }


            Expression selectExpression = ExpressionHelper.CreateSelectExpression(whereExpression, ExpressionHelper.CreatePropertyExpression(parentIdFilterNode.ReferenceFieldName, parameterExpression), parameterExpression);

            Expression distinctExpression = ExpressionHelper.CreateDistinctExpression(selectExpression);

            return distinctExpression;
        }



        private IData GetParentDataItem(Type parentType, EntityToken parentEntityToken, TreeNodeDynamicContext dynamicContext)
        {
            if (dynamicContext.CustomData.ContainsKey("ParentData") == true)
            {
                return (IData)dynamicContext.CustomData["ParentData"];
            }

            IData parentDataItem = null;
            if (parentEntityToken is DataEntityToken)
            {
                DataEntityToken dataEntityToken = parentEntityToken as DataEntityToken;
                Type type = dataEntityToken.InterfaceType;
                if (type == parentType)
                {
                    return dataEntityToken.Data;
                }
            }

            if (parentDataItem == null)
            {
                foreach (EntityToken entityToken in dynamicContext.Piggybag.GetParentEntityTokens())
                {
                    DataEntityToken dataEntityToken = entityToken as DataEntityToken;

                    if (dataEntityToken == null) continue;

                    Type type = dataEntityToken.InterfaceType;
                    if (type != parentType) continue;

                    return dataEntityToken.Data;
                }
            }

            throw new InvalidOperationException();
        }



        /// <exclude />
        public override string ToString()
        {
            return string.Format("DataElementsTreeNode, Id = {0}, DataType = {1}, Display = {2}, {3}", this.Id, this.InterfaceType, this.Display, this.ParentString());
        }



        private sealed class ParentFilterHelper
        {
            public ParentIdFilterNode ParentIdFilterNode { get; set; }
            public PropertyInfo ParentReferencedPropertyInfo { get; set; }
            public string ParentRefereePropertyName { get; set; }
        }
    }
}
