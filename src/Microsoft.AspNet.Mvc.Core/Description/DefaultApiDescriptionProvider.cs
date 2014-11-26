// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc.ModelBinding;
using Microsoft.AspNet.Routing;
using Microsoft.AspNet.Routing.Template;
using Microsoft.Net.Http.Headers;
using Microsoft.Framework.DependencyInjection;

namespace Microsoft.AspNet.Mvc.Description
{
    /// <summary>
    /// Implements a provider of <see cref="ApiDescription"/> for actions represented
    /// by <see cref="ControllerActionDescriptor"/>.
    /// </summary>
    public class DefaultApiDescriptionProvider : INestedProvider<ApiDescriptionProviderContext>
    {
        private readonly IOutputFormattersProvider _formattersProvider;
        private readonly IModelMetadataProvider _modelMetadataProvider;
        private readonly IInlineConstraintResolver _constraintResolver;

        /// <summary>
        /// Creates a new instance of <see cref="DefaultApiDescriptionProvider"/>.
        /// </summary>
        /// <param name="formattersProvider">The <see cref="IOutputFormattersProvider"/>.</param>
        /// <param name="modelMetadataProvider">The <see cref="IModelMetadataProvider"/>.</param>
        public DefaultApiDescriptionProvider(
            IOutputFormattersProvider formattersProvider,
            IInlineConstraintResolver constraintResolver,
            IModelMetadataProvider modelMetadataProvider)
        {
            _formattersProvider = formattersProvider;
            _constraintResolver = constraintResolver;
            _modelMetadataProvider = modelMetadataProvider;
        }

        /// <inheritdoc />
        public int Order
        {
            get { return DefaultOrder.DefaultFrameworkSortOrder; }
        }

        /// <inheritdoc />
        public void Invoke(ApiDescriptionProviderContext context, Action callNext)
        {
            foreach (var action in context.Actions.OfType<ControllerActionDescriptor>())
            {
                var extensionData = action.GetProperty<ApiDescriptionActionData>();
                if (extensionData != null)
                {
                    var httpMethods = GetHttpMethods(action);
                    foreach (var httpMethod in httpMethods)
                    {
                        context.Results.Add(CreateApiDescription(action, httpMethod, extensionData.GroupName));
                    }
                }
            }

            callNext();
        }

        private ApiDescription CreateApiDescription(
            ControllerActionDescriptor action,
            string httpMethod,
            string groupName)
        {
            var parsedTemplate = ParseTemplate(action);

            var apiDescription = new ApiDescription()
            {
                ActionDescriptor = action,
                GroupName = groupName,
                HttpMethod = httpMethod,
                RelativePath = GetRelativePath(parsedTemplate),
            };

            var templateParameters = parsedTemplate?.Parameters?.ToList() ?? new List<TemplatePart>();

            var parameterContext = new ApiParameterContext(_modelMetadataProvider, action, templateParameters);
            apiDescription.ParameterDescriptions.AddRange(GetParameters(parameterContext));

            var responseMetadataAttributes = GetResponseMetadataAttributes(action);

            // We only provide response info if we can figure out a type that is a user-data type.
            // Void /Task object/IActionResult will result in no data.
            var declaredReturnType = GetDeclaredReturnType(action);

            // Now 'simulate' an action execution. This attempts to figure out to the best of our knowledge
            // what the logical data type is using filters.
            var runtimeReturnType = GetRuntimeReturnType(declaredReturnType, responseMetadataAttributes);

            // We might not be able to figure out a good runtime return type. If that's the case we don't
            // provide any information about outputs. The workaround is to attribute the action.
            if (runtimeReturnType == typeof(void))
            {
                // As a special case, if the return type is void - we want to surface that information
                // specifically, but nothing else. This can be overridden with a filter/attribute.
                apiDescription.ResponseType = runtimeReturnType;
            }
            else if (runtimeReturnType != null)
            {
                apiDescription.ResponseType = runtimeReturnType;

                apiDescription.ResponseModelMetadata = _modelMetadataProvider.GetMetadataForType(
                    modelAccessor: null,
                    modelType: runtimeReturnType);

                var formats = GetResponseFormats(
                    action,
                    responseMetadataAttributes,
                    declaredReturnType,
                    runtimeReturnType);

                foreach (var format in formats)
                {
                    apiDescription.SupportedResponseFormats.Add(format);
                }
            }

            return apiDescription;
        }

        private IList<ApiParameterDescription> GetParameters(ApiParameterContext context)
        {

            if (context.ActionDescriptor.Parameters != null)
            {
                foreach (var actionParameter in context.ActionDescriptor.Parameters)
                {
                    var visitor = new PseudoModelBindingVisitor(context, actionParameter);
                    visitor.WalkParameter();
                }
            }

            return context.Results;
        }

        private IEnumerable<string> GetHttpMethods(ControllerActionDescriptor action)
        {
            if (action.ActionConstraints != null && action.ActionConstraints.Count > 0)
            {
                return action.ActionConstraints.OfType<HttpMethodConstraint>().SelectMany(c => c.HttpMethods);
            }
            else
            {
                return new string[] { null };
            }
        }

        private RouteTemplate ParseTemplate(ControllerActionDescriptor action)
        {
            if (action.AttributeRouteInfo != null &&
                action.AttributeRouteInfo.Template != null)
            {
                return TemplateParser.Parse(action.AttributeRouteInfo.Template);
            }

            return null;
        }

        private string GetRelativePath(RouteTemplate parsedTemplate)
        {
            if (parsedTemplate == null)
            {
                return null;
            }

            var segments = new List<string>();

            foreach (var segment in parsedTemplate.Segments)
            {
                var currentSegment = "";
                foreach (var part in segment.Parts)
                {
                    if (part.IsLiteral)
                    {
                        currentSegment += part.Text;
                    }
                    else if (part.IsParameter)
                    {
                        currentSegment += "{" + part.Name + "}";
                    }
                }

                segments.Add(currentSegment);
            }

            return string.Join("/", segments);
        }

        private IReadOnlyList<ApiResponseFormat> GetResponseFormats(
            ControllerActionDescriptor action,
            IApiResponseMetadataProvider[] responseMetadataAttributes,
            Type declaredType,
            Type runtimeType)
        {
            var results = new List<ApiResponseFormat>();

            // Walk through all 'filter' attributes in order, and allow each one to see or override
            // the results of the previous ones. This is similar to the execution path for content-negotiation.
            var contentTypes = new List<MediaTypeHeaderValue>();
            if (responseMetadataAttributes != null)
            {
                foreach (var metadataAttribute in responseMetadataAttributes)
                {
                    metadataAttribute.SetContentTypes(contentTypes);
                }
            }

            if (contentTypes.Count == 0)
            {
                contentTypes.Add(null);
            }

            var formatters = _formattersProvider.OutputFormatters;
            foreach (var contentType in contentTypes)
            {
                foreach (var formatter in formatters)
                {
                    var supportedTypes = formatter.GetSupportedContentTypes(declaredType, runtimeType, contentType);
                    if (supportedTypes != null)
                    {
                        foreach (var supportedType in supportedTypes)
                        {
                            results.Add(new ApiResponseFormat()
                            {
                                Formatter = formatter,
                                MediaType = supportedType,
                            });
                        }
                    }
                }
            }

            return results;
        }

        private Type GetDeclaredReturnType(ControllerActionDescriptor action)
        {
            var declaredReturnType = action.MethodInfo.ReturnType;
            if (declaredReturnType == typeof(void) ||
                declaredReturnType == typeof(Task))
            {
                return typeof(void);
            }

            // Unwrap the type if it's a Task<T>. The Task (non-generic) case was already handled.
            var unwrappedType = TypeHelper.GetTaskInnerTypeOrNull(declaredReturnType) ?? declaredReturnType;

            // If the method is declared to return IActionResult or a derived class, that information
            // isn't valuable to the formatter.
            if (typeof(IActionResult).IsAssignableFrom(unwrappedType))
            {
                return null;
            }
            else
            {
                return unwrappedType;
            }
        }

        private Type GetRuntimeReturnType(Type declaredReturnType, IApiResponseMetadataProvider[] metadataAttributes)
        {
            // Walk through all of the filter attributes and allow them to set the type. This will execute them
            // in filter-order allowing the desired behavior for overriding.
            if (metadataAttributes != null)
            {
                Type typeSetByAttribute = null;
                foreach (var metadataAttribute in metadataAttributes)
                {
                    if (metadataAttribute.Type != null)
                    {
                        typeSetByAttribute = metadataAttribute.Type;
                    }
                }

                // If one of the filters set a type, then trust it.
                if (typeSetByAttribute != null)
                {
                    return typeSetByAttribute;
                }
            }

            // If we get here, then a filter didn't give us an answer, so we need to figure out if we
            // want to use the declared return type.
            //
            // We've already excluded Task, void, and IActionResult at this point.
            //
            // If the action might return any object, then assume we don't know anything about it.
            if (declaredReturnType == typeof(object))
            {
                return null;
            }

            return declaredReturnType;
        }

        private IApiResponseMetadataProvider[] GetResponseMetadataAttributes(ControllerActionDescriptor action)
        {
            if (action.FilterDescriptors == null)
            {
                return null;
            }

            // This technique for enumerating filters will intentionally ignore any filter that is an IFilterFactory
            // while searching for a filter that implements IApiResponseMetadataProvider.
            //
            // The workaround for that is to implement the metadata interface on the IFilterFactory.
            return action.FilterDescriptors
                .Select(fd => fd.Filter)
                .OfType<IApiResponseMetadataProvider>()
                .ToArray();
        }

        private class ApiParameterContext
        {
            public ApiParameterContext(
                IModelMetadataProvider metadataProvider,
                ControllerActionDescriptor actionDescriptor,
                IReadOnlyList<TemplatePart> routeParameters)
            {
                MetadataProvider = metadataProvider;
                ActionDescriptor = actionDescriptor;
                RouteParameters = routeParameters;

                Results = new List<ApiParameterDescription>();
            }

            public ControllerActionDescriptor ActionDescriptor { get; }

            public IModelMetadataProvider MetadataProvider { get; }

            public IList<ApiParameterDescription> Results { get; }

            public IReadOnlyList<TemplatePart> RouteParameters { get; }
        }

        private class PseudoModelBindingVisitor
        {
            public PseudoModelBindingVisitor(ApiParameterContext context, ParameterDescriptor parameter)
            {
                Context = context;
                Parameter = parameter;

                Visited = new HashSet<PropertyKey>();
            }

            public ApiParameterContext Context { get; }

            public ParameterDescriptor Parameter { get; }

            // Avoid infinite recursion by tracking properties. 
            private HashSet<PropertyKey> Visited { get; }

            public void WalkParameter()
            {
                var modelMetadata = Context.MetadataProvider.GetMetadataForParameter(
                    modelAccessor: null,
                    methodInfo: Context.ActionDescriptor.MethodInfo,
                    parameterName: Parameter.Name);

                var binderMetadata = Parameter.BinderMetadata;
                if (modelMetadata != null)
                {
                    if (binderMetadata != null)
                    {
                        modelMetadata.BinderMetadata = binderMetadata;
                    }

                    var nameProvider = binderMetadata as IModelNameProvider;
                    if (nameProvider != null && nameProvider.Name != null)
                    {
                        modelMetadata.BinderModelName = nameProvider.Name;
                    }
                }

                // Attempt to find a binding source for the parameter
                if (!Visit(modelMetadata))
                {
                    // If we get here, then it means we didn't find a match for any of the model. This means that it's
                    // likely 'model-bound' in the traditional MVC sense (formdata + query string + route data) and
                    // doesn't use any IBinderMetadata.
                    // 
                    // Add a single 'default' parameter description for the model.
                    Context.Results.Add(CreateResult(modelMetadata, ApiParameterSource.ModelBinding));
                }
            }

            private bool Visit(ModelMetadata modelMetadata)
            {
                var source = GetSource(modelMetadata);
                if (source != null)
                {
                    // We have a definite answer for this model.
                    Context.Results.Add(CreateResult(modelMetadata, source));

                    return true;
                }

                // If there are no properties then we don't know how to bind this
                if (!modelMetadata.Properties.Any())
                {
                    return false;
                }

                // This will come from composite model binding - so investigate what's going on with each property.
                // 
                // Basically once we find something that we know how to bind, we want to treat all properties at that
                // level (and higher levels) as separate parameters. 
                //
                // Ex:
                //
                //      public IActionResult PlaceOrder(OrderDTO order) {...}
                //
                //      public class OrderDTO
                //      {
                //          public int AccountId { get; set; }
                //          
                //          [FromBody]
                //          public Order { get; set; }
                //      }
                //
                // This should result in two parameters:
                //
                //  AccountId - source: Any
                //  Order - source: Body
                //

                var propertyCount = 0;
                var unboundProperties = new HashSet<ModelMetadata>();
                foreach (var propertyMetadata in modelMetadata.Properties)
                {
                    propertyCount++;
                    if (Visited.Add(new PropertyKey(propertyMetadata.ContainerType, propertyMetadata.PropertyName)))
                    {
                        if (!Visit(propertyMetadata))
                        {
                            unboundProperties.Add(propertyMetadata);
                        }
                    }
                }

                if (unboundProperties.Count == propertyCount)
                {
                    // No properties were bound, let the caller handle it.
                    return false;
                }
                else
                {
                    foreach (var property in unboundProperties)
                    {
                        // Create a 'default' description for each property
                        Context.Results.Add(CreateResult(property, ApiParameterSource.ModelBinding));
                    }

                    return true;
                }
            }

            private ApiParameterDescription CreateResult(ModelMetadata metadata, ApiParameterSource source)
            {
                return new ApiParameterDescription()
                {
                    ModelMetadata = metadata,
                    Name = Parameter.Name,
                    ParameterDescriptor = Parameter,
                    Source = source,
                    Type = Parameter.ParameterType,
                };
            }


            // This isn't extensible right now.
            private static ApiParameterSource GetSource(ModelMetadata metadata)
            {
                if (metadata.BinderMetadata == null)
                {
                    // There's nothing we can figure out.
                    return null;
                }

                if (metadata.BinderMetadata is IFormatterBinderMetadata)
                {
                    return ApiParameterSource.Body;
                }
                else if (metadata.BinderMetadata is IRouteDataValueProviderMetadata)
                {
                    return ApiParameterSource.Path;
                }
                else if (metadata.BinderMetadata is IQueryValueProviderMetadata)
                {
                    return ApiParameterSource.Query;
                }
                else if (metadata.BinderMetadata is IHeaderBinderMetadata)
                {
                    return ApiParameterSource.Header;
                }
                else if (metadata.BinderMetadata is IServiceActivatorBinderMetadata)
                {
                    return ApiParameterSource.Hidden;
                }

                var binderTypeMetadata = metadata.BinderMetadata as IBinderTypeProviderMetadata;
                if (binderTypeMetadata != null && binderTypeMetadata.BinderType != null)
                {
                    // This provides it's own model binder, so we can't really make a good
                    // estimate of where it comes from.
                    return ApiParameterSource.Unknown;
                }

                // We're out of cases we know how to handle.
                return null;
            }

            private struct PropertyKey
            {
                public readonly Type ContainerType;

                public readonly string PropertyName;

                public PropertyKey(Type containerType, string propertyName)
                {
                    ContainerType = containerType;
                    PropertyName = propertyName;
                }
            }
        }
    }
}