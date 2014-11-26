﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc.ModelBinding;
using Microsoft.AspNet.Mvc.Routing;
using Microsoft.AspNet.Routing;
using Microsoft.AspNet.Routing.Constraints;
using Microsoft.Net.Http.Headers;
using Moq;
using Xunit;

namespace Microsoft.AspNet.Mvc.Description
{
    public class DefaultApiDescriptionProviderTest
    {
        [Fact]
        public void GetApiDescription_IgnoresNonReflectedActionDescriptor()
        {
            // Arrange
            var action = new ActionDescriptor();
            action.SetProperty(new ApiDescriptionActionData());

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            Assert.Empty(descriptions);
        }

        [Fact]
        public void GetApiDescription_IgnoresActionWithoutApiExplorerData()
        {
            // Arrange
            var action = new ControllerActionDescriptor();

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            Assert.Empty(descriptions);
        }

        [Fact]
        public void GetApiDescription_PopulatesActionDescriptor()
        {
            // Arrange
            var action = CreateActionDescriptor();

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Same(action, description.ActionDescriptor);
        }

        [Fact]
        public void GetApiDescription_PopulatesGroupName()
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.GetProperty<ApiDescriptionActionData>().GroupName = "Customers";

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal("Customers", description.GroupName);
        }

        [Fact]
        public void GetApiDescription_HttpMethodIsNullWithoutConstraint()
        {
            // Arrange
            var action = CreateActionDescriptor();

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Null(description.HttpMethod);
        }


        [Fact]
        public void GetApiDescription_CreatesMultipleDescriptionsForMultipleHttpMethods()
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.ActionConstraints = new List<IActionConstraintMetadata>()
            {
                new HttpMethodConstraint(new string[] { "PUT", "POST" }),
                new HttpMethodConstraint(new string[] { "GET" }),
            };

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            Assert.Equal(3, descriptions.Count);

            Assert.Single(descriptions, d => d.HttpMethod == "PUT");
            Assert.Single(descriptions, d => d.HttpMethod == "POST");
            Assert.Single(descriptions, d => d.HttpMethod == "GET");
        }

        // This is a test for the placeholder behavior - see #886
        [Fact]
        public void GetApiDescription_PopulatesParameters()
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.Parameters = new List<ParameterDescriptor>()
            {
                new ParameterDescriptor()
                {
                    Name = "id",
                    ParameterType = typeof(int),
                },
                new ParameterDescriptor()
                {
                    BinderMetadata = new FromBodyAttribute(),
                    Name = "username",
                    ParameterType = typeof(string),
                }
            };

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal(2, description.ParameterDescriptions.Count);

            var id = Assert.Single(description.ParameterDescriptions, p => p.Name == "id");
            Assert.NotNull(id.ModelMetadata);
            Assert.False(id.IsOptional);
            Assert.Same(action.Parameters[0], id.ParameterDescriptor);
            Assert.Equal(ApiParameterSource.Query, id.Source);
            Assert.Equal(typeof(int), id.Type);

            var username = Assert.Single(description.ParameterDescriptions, p => p.Name == "username");
            Assert.NotNull(username.ModelMetadata);
            Assert.False(username.IsOptional);
            Assert.Same(action.Parameters[1], username.ParameterDescriptor);
            Assert.Equal(ApiParameterSource.Body, username.Source);
            Assert.Equal(typeof(string), username.Type);
        }

        [Theory]
        [InlineData("api/products/{id}", false, null, null)]
        [InlineData("api/products/{id?}", true, null, null)]
        [InlineData("api/products/{id=5}", true, null, "5")]
        [InlineData("api/products/{id:int}", false, typeof(IntRouteConstraint), null)]
        [InlineData("api/products/{id:int?}", true, typeof(IntRouteConstraint), null)]
        [InlineData("api/products/{id:int=5}", true, null, "5")]
        [InlineData("api/products/{*id}", false, null, null)]
        [InlineData("api/products/{*id:int}", false, typeof(IntRouteConstraint), null)]
        [InlineData("api/products/{*id:int=5}", true, typeof(IntRouteConstraint), "5")]
        public void GetApiDescription_PopulatesParameters_ThatAppearOnlyOnRouteTemplate(
            string template,
            bool isOptional,
            Type constraintType,
            object defaultValue)
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.AttributeRouteInfo = new AttributeRouteInfo { Template = template };

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal(ApiParameterSource.Path, parameter.Source);
            Assert.Equal(isOptional, parameter.IsOptional);
            Assert.Equal("id", parameter.Name);
            Assert.Null(parameter.ParameterDescriptor);

            if (constraintType != null)
            {
                Assert.IsType(constraintType, Assert.Single(parameter.Constraints));
            }

            if (defaultValue != null)
            {
                Assert.Equal(defaultValue, parameter.DefaultValue);
            }
            else
            {
                Assert.Null(parameter.DefaultValue);
            }
        }

        [Theory]
        [InlineData("api/products/{id}", false, null, null)]
        [InlineData("api/products/{id?}", true, null, null)]
        [InlineData("api/products/{id=5}", true, null, "5")]
        [InlineData("api/products/{id:int}", false, typeof(IntRouteConstraint), null)]
        [InlineData("api/products/{id:int?}", true, typeof(IntRouteConstraint), null)]
        [InlineData("api/products/{id:int=5}", true, typeof(IntRouteConstraint), "5")]
        [InlineData("api/products/{*id}", false, null, null)]
        [InlineData("api/products/{*id:int}", false, typeof(IntRouteConstraint), null)]
        [InlineData("api/products/{*id:int=5}", true, typeof(IntRouteConstraint), "5")]
        public void GetApiDescription_PopulatesParametersThatAppearOnRouteTemplate_AndHaveAssociatedParameterDescriptor(
            string template,
            bool isOptional,
            Type constraintType,
            object defaultValue)
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.AttributeRouteInfo = new AttributeRouteInfo { Template = template };

            var parameterDescriptor = new ParameterDescriptor
            {
                Name = "id",
                ParameterType = typeof(int),
            };
            action.Parameters = new List<ParameterDescriptor> { parameterDescriptor };

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal(ApiParameterSource.Path, parameter.Source);
            Assert.Equal(isOptional, parameter.IsOptional);
            Assert.Equal("id", parameter.Name);
            Assert.Equal(parameterDescriptor, parameter.ParameterDescriptor);

            if (constraintType != null)
            {
                Assert.IsType(constraintType, Assert.Single(parameter.Constraints));
            }

            if (defaultValue != null)
            {
                Assert.Equal(defaultValue, parameter.DefaultValue);
            }
            else
            {
                Assert.Null(parameter.DefaultValue);
            }
        }

        [Theory]
        [InlineData("api/products/{id}", false, null, null)]
        [InlineData("api/products/{id?}", true, null, null)]
        [InlineData("api/products/{id=5}", true, null, "5")]
        [InlineData("api/products/{id:int}", false, typeof(IntRouteConstraint), null)]
        [InlineData("api/products/{id:int?}", true, typeof(IntRouteConstraint), null)]
        [InlineData("api/products/{id:int=5}", true, typeof(IntRouteConstraint), "5")]
        [InlineData("api/products/{*id}", false, null, null)]
        [InlineData("api/products/{*id:int}", false, typeof(IntRouteConstraint), null)]
        [InlineData("api/products/{*id:int=5}", true, typeof(IntRouteConstraint), "5")]
        public void GetApiDescription_CreatesDifferentParameters_IfParameterDescriptorIsFromBody(
            string template,
            bool isOptional,
            Type constraintType,
            object defaultValue)
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.AttributeRouteInfo = new AttributeRouteInfo { Template = template };

            var parameterDescriptor = new ParameterDescriptor
            {
                BinderMetadata = new FromBodyAttribute(),
                Name = "id",
                ParameterType = typeof(int),
            };
            action.Parameters = new List<ParameterDescriptor> { parameterDescriptor };

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var bodyParameter = Assert.Single(description.ParameterDescriptions, p => p.Source == ApiParameterSource.Body);
            Assert.False(bodyParameter.IsOptional);
            Assert.Equal("id", bodyParameter.Name);
            Assert.Equal(parameterDescriptor, bodyParameter.ParameterDescriptor);

            var pathParameter = Assert.Single(description.ParameterDescriptions, p => p.Source == ApiParameterSource.Path);
            Assert.Equal(isOptional, pathParameter.IsOptional);
            Assert.Equal("id", pathParameter.Name);
            Assert.Null(pathParameter.ParameterDescriptor);

            if (constraintType != null)
            {
                Assert.IsType(constraintType, Assert.Single(pathParameter.Constraints));
            }

            if (defaultValue != null)
            {
                Assert.Equal(defaultValue, pathParameter.DefaultValue);
            }
            else
            {
                Assert.Null(pathParameter.DefaultValue);
            }
        }

        [Theory]
        [InlineData("api/products/{id}", false)]
        [InlineData("api/products/{id?}", true)]
        [InlineData("api/products/{id=5}", true)]
        public void GetApiDescription_ParameterFromPathAndDescriptor_IsOptionalIfRouteParameterIsOptional(
            string template,
            bool expectedOptional)
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.AttributeRouteInfo = new AttributeRouteInfo { Template = template };

            var parameterDescriptor = new ParameterDescriptor
            {
                Name = "id",
                ParameterType = typeof(int),
            };
            action.Parameters = new List<ParameterDescriptor> { parameterDescriptor };

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal(expectedOptional, parameter.IsOptional);
        }

        [Theory]
        [InlineData("api/Products/{id}", "api/Products/{id}")]
        [InlineData("api/Products/{id?}", "api/Products/{id}")]
        [InlineData("api/Products/{id:int}", "api/Products/{id}")]
        [InlineData("api/Products/{id:int?}", "api/Products/{id}")]
        [InlineData("api/Products/{*id}", "api/Products/{id}")]
        [InlineData("api/Products/{*id:int}", "api/Products/{id}")]
        [InlineData("api/Products/{id1}-{id2:int}", "api/Products/{id1}-{id2}")]
        [InlineData("api/{id1}/{id2?}/{id3:int}/{id4:int?}/{*id5:int}", "api/{id1}/{id2}/{id3}/{id4}/{id5}")]
        public void GetApiDescription_PopulatesRelativePath(string template, string relativePath)
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.AttributeRouteInfo = new AttributeRouteInfo();
            action.AttributeRouteInfo.Template = template;

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal(relativePath, description.RelativePath);
        }

        [Fact]
        public void GetApiDescription_DetectsMultipleParameters_OnTheSameSegment()
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.AttributeRouteInfo = new AttributeRouteInfo();
            action.AttributeRouteInfo.Template = "api/Products/{id1}-{id2:int}";

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            var id1 = Assert.Single(description.ParameterDescriptions, p => p.Name == "id1");
            Assert.Equal(ApiParameterSource.Path, id1.Source);
            Assert.Empty(id1.Constraints);

            var id2 = Assert.Single(description.ParameterDescriptions, p => p.Name == "id2");
            Assert.Equal(ApiParameterSource.Path, id2.Source);
            Assert.IsType<IntRouteConstraint>(Assert.Single(id2.Constraints));
        }

        [Fact]
        public void GetApiDescription_DetectsMultipleParameters_OnDifferentSegments()
        {
            // Arrange
            var action = CreateActionDescriptor();
            action.AttributeRouteInfo = new AttributeRouteInfo();
            action.AttributeRouteInfo.Template = "api/Products/{id1}-{id2}/{id3:int}/{id4:int?}/{*id5:int}";

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            Assert.Single(description.ParameterDescriptions, p => p.Name == "id1");
            Assert.Single(description.ParameterDescriptions, p => p.Name == "id2");
            Assert.Single(description.ParameterDescriptions, p => p.Name == "id3");
            Assert.Single(description.ParameterDescriptions, p => p.Name == "id4");
            Assert.Single(description.ParameterDescriptions, p => p.Name == "id5");
        }

        [Fact]
        public void GetApiDescription_PopulatesResponseType_WithProduct()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(ReturnsProduct));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal(typeof(Product), description.ResponseType);
            Assert.NotNull(description.ResponseModelMetadata);
        }

        [Fact]
        public void GetApiDescription_PopulatesResponseType_WithTaskOfProduct()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(ReturnsTaskOfProduct));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal(typeof(Product), description.ResponseType);
            Assert.NotNull(description.ResponseModelMetadata);
        }

        [Theory]
        [InlineData(nameof(ReturnsObject))]
        [InlineData(nameof(ReturnsActionResult))]
        [InlineData(nameof(ReturnsJsonResult))]
        [InlineData(nameof(ReturnsTaskOfObject))]
        [InlineData(nameof(ReturnsTaskOfActionResult))]
        [InlineData(nameof(ReturnsTaskOfJsonResult))]
        public void GetApiDescription_DoesNotPopulatesResponseInformation_WhenUnknown(string methodName)
        {
            // Arrange
            var action = CreateActionDescriptor(methodName);

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Null(description.ResponseType);
            Assert.Null(description.ResponseModelMetadata);
            Assert.Empty(description.SupportedResponseFormats);
        }

        [Theory]
        [InlineData(nameof(ReturnsVoid))]
        [InlineData(nameof(ReturnsTask))]
        public void GetApiDescription_DoesNotPopulatesResponseInformation_WhenVoid(string methodName)
        {
            // Arrange
            var action = CreateActionDescriptor(methodName);

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal(typeof(void), description.ResponseType);
            Assert.Null(description.ResponseModelMetadata);
            Assert.Empty(description.SupportedResponseFormats);
        }

        [Theory]
        [InlineData(nameof(ReturnsObject))]
        [InlineData(nameof(ReturnsVoid))]
        [InlineData(nameof(ReturnsActionResult))]
        [InlineData(nameof(ReturnsJsonResult))]
        [InlineData(nameof(ReturnsTaskOfObject))]
        [InlineData(nameof(ReturnsTask))]
        [InlineData(nameof(ReturnsTaskOfActionResult))]
        [InlineData(nameof(ReturnsTaskOfJsonResult))]
        public void GetApiDescription_PopulatesResponseInformation_WhenSetByFilter(string methodName)
        {
            // Arrange
            var action = CreateActionDescriptor(methodName);
            var filter = new ContentTypeAttribute("text/*")
            {
                Type = typeof(Order)
            };

            action.FilterDescriptors = new List<FilterDescriptor>();
            action.FilterDescriptors.Add(new FilterDescriptor(filter, FilterScope.Action));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal(typeof(Order), description.ResponseType);
            Assert.NotNull(description.ResponseModelMetadata);
        }

        [Fact]
        public void GetApiDescription_IncludesResponseFormats()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(ReturnsProduct));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal(4, description.SupportedResponseFormats.Count);

            var formats = description.SupportedResponseFormats;
            Assert.Single(formats, f => f.MediaType.ToString() == "text/json");
            Assert.Single(formats, f => f.MediaType.ToString() == "application/json");
            Assert.Single(formats, f => f.MediaType.ToString() == "text/xml");
            Assert.Single(formats, f => f.MediaType.ToString() == "application/xml");
        }

        [Fact]
        public void GetApiDescription_IncludesResponseFormats_FilteredByAttribute()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(ReturnsProduct));

            action.FilterDescriptors = new List<FilterDescriptor>();
            action.FilterDescriptors.Add(new FilterDescriptor(new ContentTypeAttribute("text/*"), FilterScope.Action));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal(2, description.SupportedResponseFormats.Count);

            var formats = description.SupportedResponseFormats;
            Assert.Single(formats, f => f.MediaType.ToString() == "text/json");
            Assert.Single(formats, f => f.MediaType.ToString() == "text/xml");
        }

        [Fact]
        public void GetApiDescription_IncludesResponseFormats_FilteredByType()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(ReturnsObject));
            var filter = new ContentTypeAttribute("text/*")
            {
                Type = typeof(Order)
            };

            action.FilterDescriptors = new List<FilterDescriptor>();
            action.FilterDescriptors.Add(new FilterDescriptor(filter, FilterScope.Action));

            var formatters = CreateFormatters();

            // This will just format Order
            formatters[0].SupportedTypes.Add(typeof(Order));

            // This will just format Product
            formatters[1].SupportedTypes.Add(typeof(Product));

            // Act
            var descriptions = GetApiDescriptions(action, formatters);

            // Assert
            var description = Assert.Single(descriptions);
            Assert.Equal(1, description.SupportedResponseFormats.Count);
            Assert.Equal(typeof(Order), description.ResponseType);
            Assert.NotNull(description.ResponseModelMetadata);

            var formats = description.SupportedResponseFormats;
            Assert.Single(formats, f => f.MediaType.ToString() == "text/json");
            Assert.Same(formatters[0], formats[0].Formatter);
        }

        [Fact]
        public void GetApiDescription_ParameterDescription_ModelBoundParameter()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(AcceptsProduct));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal("product", parameter.Name);
            Assert.Same(ApiParameterSource.ModelBinding, parameter.Source);
        }

        [Fact]
        public void GetApiDescription_ParameterDescription_SourceFromRouteData()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(AcceptsId_Route));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal("id", parameter.Name);
            Assert.Same(ApiParameterSource.Path, parameter.Source);
        }

        [Fact]
        public void GetApiDescription_ParameterDescription_SourceFromQueryString()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(AcceptsId_Query));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal("id", parameter.Name);
            Assert.Same(ApiParameterSource.Query, parameter.Source);
        }

        [Fact]
        public void GetApiDescription_ParameterDescription_SourceFromBody()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(AcceptsProduct_Body));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal("product", parameter.Name);
            Assert.Same(ApiParameterSource.Body, parameter.Source);
        }

        [Fact]
        public void GetApiDescription_ParameterDescription_SourceFromHeader()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(AcceptsId_Header));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal("id", parameter.Name);
            Assert.Same(ApiParameterSource.Header, parameter.Source);
        }

        [Fact]
        public void GetApiDescription_ParameterDescription_SourceFromServices()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(AcceptsFormatters_Services));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal("formatters", parameter.Name);
            Assert.Same(ApiParameterSource.Hidden, parameter.Source);
        }

        [Fact]
        public void GetApiDescription_ParameterDescription_SourceFromCustomModelBinder()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(AcceptsProduct_Custom));

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal("product", parameter.Name);
            Assert.Same(ApiParameterSource.Unknown, parameter.Source);
        }

        [Fact]
        public void GetApiDescription_ParameterDescription_SourceFromDefault_ModelBinderAttribute_WithoutBinderType()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(AcceptsProduct_Default));

            // Act
            System.Diagnostics.Debugger.Launch();
            System.Diagnostics.Debugger.Break();

            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var parameter = Assert.Single(description.ParameterDescriptions);
            Assert.Equal("product", parameter.Name);
            Assert.Same(ApiParameterSource.ModelBinding, parameter.Source);
        }

        [Fact]
        public void GetApiDescription_ParameterDescription_ComplexDTO()
        {
            // Arrange
            var action = CreateActionDescriptor(nameof(AcceptsProductChangeDTO));
            var parameterDescriptor = action.Parameters.Single();

            // Act
            var descriptions = GetApiDescriptions(action);

            // Assert
            var description = Assert.Single(descriptions);

            var id = Assert.Single(description.ParameterDescriptions, p => p.Name == "Id");
            Assert.Same(parameterDescriptor, id.ParameterDescriptor);
            Assert.Same(ApiParameterSource.Path, id.Source);
            Assert.Equal(typeof(int), id.Type);

            var product = Assert.Single(description.ParameterDescriptions, p => p.Name == "Product");
            Assert.Same(parameterDescriptor, product.ParameterDescriptor);
            Assert.Same(ApiParameterSource.Body, product.Source);
            Assert.Equal(typeof(Product), product.Type);

            var userId = Assert.Single(description.ParameterDescriptions, p => p.Name == "UserId");
            Assert.Same(parameterDescriptor, userId.ParameterDescriptor);
            Assert.Same(ApiParameterSource.Header, userId.Source);
            Assert.Equal(typeof(string), userId.Type);

            var comments = Assert.Single(description.ParameterDescriptions, p => p.Name == "Comments");
            Assert.Same(parameterDescriptor, comments.ParameterDescriptor);
            Assert.Same(ApiParameterSource.ModelBinding, comments.Source);
            Assert.Equal(typeof(string), comments.Type);
        }

        private IReadOnlyList<ApiDescription> GetApiDescriptions(ActionDescriptor action)
        {
            return GetApiDescriptions(action, CreateFormatters());
        }

        private IReadOnlyList<ApiDescription> GetApiDescriptions(
            ActionDescriptor action,
            List<MockFormatter> formatters)
        {
            var context = new ApiDescriptionProviderContext(new ActionDescriptor[] { action });

            var formattersProvider = new Mock<IOutputFormattersProvider>(MockBehavior.Strict);
            formattersProvider.Setup(fp => fp.OutputFormatters).Returns(formatters);

            var constraintResolver = new Mock<IInlineConstraintResolver>();
            constraintResolver.Setup(c => c.ResolveConstraint("int"))
                .Returns(new IntRouteConstraint());

            var modelMetadataProvider = new EmptyModelMetadataProvider();

            var provider = new DefaultApiDescriptionProvider(
                formattersProvider.Object,
                constraintResolver.Object,
                modelMetadataProvider);

            provider.Invoke(context, () => { });
            return context.Results;
        }

        private List<MockFormatter> CreateFormatters()
        {
            // Include some default formatters that look reasonable, some tests will override this.
            var formatters = new List<MockFormatter>()
            {
                new MockFormatter(),
                new MockFormatter(),
            };

            formatters[0].SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json"));
            formatters[0].SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/json"));

            formatters[1].SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/xml"));
            formatters[1].SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/xml"));

            return formatters;
        }

        private ControllerActionDescriptor CreateActionDescriptor(string methodName = null)
        {
            var action = new ControllerActionDescriptor();
            action.SetProperty(new ApiDescriptionActionData());

            action.MethodInfo = GetType().GetMethod(
                methodName ?? "ReturnsObject",
                BindingFlags.Instance | BindingFlags.NonPublic);

            action.Parameters = new List<ParameterDescriptor>();

            foreach (var parameter in action.MethodInfo.GetParameters())
            {
                action.Parameters.Add(new ParameterDescriptor()
                {
                    BinderMetadata = parameter.GetCustomAttributes().OfType<IBinderMetadata>().FirstOrDefault(),
                    Name = parameter.Name,
                    ParameterType = parameter.ParameterType,
                });
            }

            return action;
        }

        private object ReturnsObject()
        {
            return null;
        }

        private void ReturnsVoid()
        {

        }

        private IActionResult ReturnsActionResult()
        {
            return null;
        }

        private JsonResult ReturnsJsonResult()
        {
            return null;
        }

        private Task<Product> ReturnsTaskOfProduct()
        {
            return null;
        }

        private Task<object> ReturnsTaskOfObject()
        {
            return null;
        }

        private Task ReturnsTask()
        {
            return null;
        }

        private Task<IActionResult> ReturnsTaskOfActionResult()
        {
            return null;
        }

        private Task<JsonResult> ReturnsTaskOfJsonResult()
        {
            return null;
        }

        private Product ReturnsProduct()
        {
            return null;
        }

        private void AcceptsProduct(Product product)
        {
        }

        private void AcceptsProduct_Body([FromBody] Product product)
        {
        }

        // This will show up as source = model binding
        private void AcceptsProduct_Default([ModelBinder] Product product)
        {
        }

        // This will show up as source = unknown
        private void AcceptsProduct_Custom([ModelBinder(BinderType = typeof(BodyModelBinder))] Product product)
        {
        }

        private void AcceptsId_Route([FromRoute] int id)
        {
        }

        private void AcceptsId_Query([FromQuery] int id)
        {
        }

        private void AcceptsId_Header([FromHeader] int id)
        {
        }

        private void AcceptsFormatters_Services([FromServices] IOutputFormattersProvider formatters)
        {
        }

        private void AcceptsProductChangeDTO(ProductChangeDTO dto)
        {
        }

        private class Product
        {
            public int ProductId { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }
        }

        private class Order
        {
            public int OrderId { get; set; }

            public int ProductId { get; set; }

            public int Quantity { get; set; }

            public decimal Price { get; set; }
        }

        private class ProductChangeDTO
        {
            [FromRoute]
            public int Id { get; set; }

            [FromBody]
            public Product Product { get; set; }

            [FromHeader]
            public string UserId { get; set; }

            public string Comments { get; set; }
        }

        private class MockFormatter : OutputFormatter
        {
            public List<Type> SupportedTypes { get; } = new List<Type>();

            public override Task WriteResponseBodyAsync(OutputFormatterContext context)
            {
                throw new NotImplementedException();
            }

            protected override bool CanWriteType(Type declaredType, Type actualType)
            {
                if (SupportedTypes.Count == 0)
                {
                    return true;
                }
                else if ((actualType ?? declaredType) == null)
                {
                    return false;
                }
                else
                {
                    return SupportedTypes.Contains(actualType ?? declaredType);
                }
            }
        }

        private class ContentTypeAttribute : Attribute, IFilter, IApiResponseMetadataProvider
        {
            public ContentTypeAttribute(string mediaType)
            {
                ContentTypes.Add(MediaTypeHeaderValue.Parse(mediaType));
            }

            public List<MediaTypeHeaderValue> ContentTypes { get; } = new List<MediaTypeHeaderValue>();

            public Type Type { get; set; }

            public void SetContentTypes(IList<MediaTypeHeaderValue> contentTypes)
            {
                contentTypes.Clear();
                foreach (var contentType in ContentTypes)
                {
                    contentTypes.Add(contentType);
                }
            }
        }
    }
}