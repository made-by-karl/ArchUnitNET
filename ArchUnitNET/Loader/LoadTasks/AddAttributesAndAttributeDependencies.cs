//  Copyright 2019 Florian Gather <florian.gather@tngtech.com>
// 	Copyright 2019 Paula Ruiz <paularuiz22@gmail.com>
// 	Copyright 2019 Fritz Brandhuber <fritz.brandhuber@tngtech.com>
// 
// 	SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using ArchUnitNET.Domain;
using ArchUnitNET.Domain.Dependencies;
using ArchUnitNET.Domain.Extensions;
using JetBrains.Annotations;
using Mono.Cecil;

namespace ArchUnitNET.Loader.LoadTasks
{
    internal class AddAttributesAndAttributeDependencies : ILoadTask
    {
        private readonly IType _type;
        private readonly TypeDefinition _typeDefinition;
        private readonly TypeFactory _typeFactory;

        public AddAttributesAndAttributeDependencies(IType type, TypeDefinition typeDefinition, TypeFactory typeFactory)
        {
            _type = type;
            _typeDefinition = typeDefinition;
            _typeFactory = typeFactory;
        }

        public void Execute()
        {
            _typeDefinition.CustomAttributes.ForEach(AddAttributeArgumentReferenceDependenciesToOriginType);
            var typeAttributes = CreateAttributesFromCustomAttributes(_typeDefinition.CustomAttributes).ToList();
            _type.Attributes.AddRange(typeAttributes);
            var typeAttributeDependencies =
                typeAttributes.Select(attribute => new AttributeTypeDependency(_type, attribute));
            _type.Dependencies.AddRange(typeAttributeDependencies);
            SetUpAttributesForTypeGenericParameters();
            CollectAttributesForMembers();
        }

        private void SetUpAttributesForTypeGenericParameters()
        {
            foreach (var genericParameter in _typeDefinition.GenericParameters)
            {
                var param = _type.GenericParameters.First(parameter => parameter.Name == genericParameter.Name);
                var attributes = CreateAttributesFromCustomAttributes(genericParameter.CustomAttributes).ToList();
                _type.Attributes.AddRange(attributes);
                param.Attributes.AddRange(attributes);
                var genericParameterAttributeDependencies = attributes.Select(attribute =>
                    new AttributeTypeGenericParameterDependency(_type, param, attribute));
                _type.Dependencies.AddRange(genericParameterAttributeDependencies);
            }
        }

        private void CollectAttributesForMembers()
        {
            _typeDefinition.Fields.Where(x => !x.IsBackingField()).ForEach(SetUpAttributesForFields);

            _typeDefinition.Properties.ForEach(SetUpAttributesForProperties);

            _typeDefinition.Methods.ForEach(SetUpAttributesForMethods);
        }

        private void SetUpAttributesForFields(FieldDefinition fieldDefinition)
        {
            var fieldMember = _type.GetFieldMembers().WhereFullNameIs(fieldDefinition.FullName)
                .RequiredNotNull();
            CollectMemberAttributesAndDependencies(fieldMember, fieldDefinition.CustomAttributes.ToList(),
                fieldMember.MemberDependencies);
        }

        private void SetUpAttributesForProperties(PropertyDefinition propertyDefinition)
        {
            var propertyMember = _type.GetPropertyMembers().WhereFullNameIs(propertyDefinition.FullName)
                .RequiredNotNull();
            CollectMemberAttributesAndDependencies(propertyMember, propertyDefinition.CustomAttributes.ToList(),
                propertyMember.AttributeDependencies);
        }

        private void SetUpAttributesForMethods(MethodDefinition methodDefinition)
        {
            var methodMember = _type.GetMethodMembers().WhereFullNameIs(methodDefinition.GetFullName())
                .RequiredNotNull();
            var memberCustomAttributes = methodDefinition.GetAllMethodCustomAttributes().ToList();
            SetUpAttributesForMethodGenericParameters(methodDefinition, methodMember);
            CollectMemberAttributesAndDependencies(methodMember, memberCustomAttributes,
                methodMember.MemberDependencies);
        }

        private void SetUpAttributesForMethodGenericParameters(MethodDefinition methodDefinition,
            MethodMember methodMember)
        {
            foreach (var genericParameter in methodDefinition.GenericParameters)
            {
                var param = methodMember.GenericParameters.First(parameter => parameter.Name == genericParameter.Name);
                var customAttributes = genericParameter.CustomAttributes;
                customAttributes.ForEach(AddAttributeArgumentReferenceDependenciesToOriginType);
                var attributes = CreateAttributesFromCustomAttributes(customAttributes).ToList();
                methodMember.Attributes.AddRange(attributes);
                param.Attributes.AddRange(attributes);
                var genericParameterAttributeDependencies = attributes.Select(attribute =>
                    new AttributeMemberGenericParameterDependency(methodMember, param, attribute));
                methodMember.MemberDependencies.AddRange(genericParameterAttributeDependencies);
            }
        }

        private void CollectMemberAttributesAndDependencies(IMember methodMember,
            List<CustomAttribute> memberCustomAttributes, List<IMemberTypeDependency> attributeDependencies)
        {
            memberCustomAttributes.ForEach(AddAttributeArgumentReferenceDependenciesToOriginType);
            var memberAttributes = CreateAttributesFromCustomAttributes(memberCustomAttributes).ToList();
            methodMember.Attributes.AddRange(memberAttributes);
            var methodAttributeDependencies = CreateMemberAttributeDependencies(methodMember, memberAttributes);
            attributeDependencies.AddRange(methodAttributeDependencies);
        }

        [NotNull]
        private IEnumerable<Attribute> CreateAttributesFromCustomAttributes(
            IEnumerable<CustomAttribute> customAttributes)
        {
            return customAttributes.Select(customAttribute =>
            {
                var attributeTypeReference = customAttribute.AttributeType;
                var attributeType = _typeFactory.GetOrCreateStubTypeFromTypeReference(attributeTypeReference);
                return new Attribute(attributeType as Class);
            });
        }

        [NotNull]
        private static IEnumerable<AttributeMemberDependency> CreateMemberAttributeDependencies(IMember member,
            IEnumerable<Attribute> attributes)
        {
            return attributes.Select(attribute => new AttributeMemberDependency(member, attribute));
        }

        private void AddAttributeArgumentReferenceDependenciesToOriginType(ICustomAttribute customAttribute)
        {
            if (!customAttribute.HasConstructorArguments)
            {
                return;
            }

            var attributeConstructorArgs = customAttribute.ConstructorArguments;
            attributeConstructorArgs
                .Where(attributeArgument => attributeArgument.Value is TypeReference)
                .Select(attributeArgument => (typeReference: attributeArgument.Value as TypeReference,
                    attributeArgument))
                .ForEach(tuple =>
                {
                    var argumentType = _typeFactory.GetOrCreateStubTypeFromTypeReference(tuple.typeReference);
                    var dependency = new TypeReferenceDependency(_type, argumentType);
                    _type.Dependencies.Add(dependency);
                });
        }
    }
}