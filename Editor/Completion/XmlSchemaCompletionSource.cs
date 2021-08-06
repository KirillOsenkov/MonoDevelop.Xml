using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using MonoDevelop.Xml.Dom;

namespace MonoDevelop.Xml.Editor.Completion
{
	public class XmlSchemaCompletionSource : XmlCompletionSource
	{
		protected XmlSchema lastSearchedSchema = null;

		public XmlSchemaCompletionSource (ITextView textView, XmlSchema schema) : base(textView, schema)
		{}

		/// <summary>
		/// Converts the element to a complex type if possible.
		/// </summary>
		XmlSchemaComplexType GetElementAsComplexType (XmlSchemaElement element)
		{
			return (element.SchemaType as XmlSchemaComplexType)
				?? FindNamedType (schema, element.SchemaTypeName);
		}

		#region CompletionSource overrides
		protected override Task<CompletionContext> GetElementCompletionsAsync (
			IAsyncCompletionSession session,
			SnapshotPoint triggerLocation,
			List<XObject> nodePath,
			bool includeBracket,
			CancellationToken token
			)
		{
			var node = nodePath.Last () as XElement;
			var xmlPath = XmlElementPath.Resolve (nodePath);
			if (node != null) {
				var list = new XmlSchemaCompletionBuilder (this);
				var element = FindElement (xmlPath);
				if (element != null)
					GetChildElementCompletionData (list, element, "");
				return Task.FromResult(new CompletionContext (list.GetItems ()));
			}
			return Task.FromResult (CompletionContext.Empty);
		}

		protected override Task<CompletionContext> GetAttributeCompletionsAsync (
			IAsyncCompletionSession session,
			SnapshotPoint triggerLocation,
			List<XObject> nodePath,
			IAttributedXObject attributedObject,
			Dictionary<string, string> existingAtts,
			CancellationToken token
			)
		{
			// TODO: use `attributedObject` parameter for efficiency?
			var node = nodePath.Last () as XElement;
			var xmlPath = XmlElementPath.Resolve (nodePath);
			if (node != null) {
				var list = new XmlSchemaCompletionBuilder (this);
				//var element = FindElement (node.Name.Name);
				var element = FindElement (xmlPath);
				if (element != null)
					GetAttributeCompletionData (list, element);
				return Task.FromResult (new CompletionContext (list.GetItems ()));
			}
			return Task.FromResult (CompletionContext.Empty);
		}

		protected override Task<CompletionContext> GetAttributeValueCompletionsAsync (
			IAsyncCompletionSession session,
			SnapshotPoint triggerLocation,
			List<XObject> nodePath,
			IAttributedXObject attributedObject,
			XAttribute attribute,
			CancellationToken token
			)
		{
			var node = nodePath.Last () as XElement;
			var xmlPath = XmlElementPath.Resolve (nodePath);
			if (node != null) {
				var list = new XmlSchemaCompletionBuilder (this);
				var element = FindElement (xmlPath);
				if (element != null) {
					var xmlAttribute = FindAttribute (GetElementAsComplexType (element), attribute.Name.Name);
					if (xmlAttribute != null) {
						GetAttributeValueCompletionData (list, xmlAttribute);
					}
				}
				return Task.FromResult (new CompletionContext (list.GetItems ()));
			}
			return Task.FromResult (CompletionContext.Empty);
		}

		#endregion

		#region ChildElementCompletion
		/// <summary>
		/// Gets the child element completion data for the xml element that exists
		/// at the end of the specified path.
		/// </summary>
		public Task<CompletionContext> GetChildElementCompletionDataAsync (IAsyncCompletionSource source, XmlElementPath path, CancellationToken token)
		{
			var builder = new XmlSchemaCompletionBuilder (source, path.Namespaces);
			var element = FindElement (path);
			if (element != null) {
				var last = path.Elements.LastOrDefault ();
				GetChildElementCompletionData (builder, element, last != null ? last.Prefix : "");
			}
			return Task.FromResult(new CompletionContext (builder.GetItems ()));
		}

		void GetChildElementCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaElement element, string prefix)
		{
			var complexType = GetElementAsComplexType (element);
			if (complexType != null)
				GetChildElementCompletionData (data, complexType, prefix);
		}

		void GetChildElementCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaComplexType complexType, string prefix)
		{
			if (complexType.Particle is XmlSchemaSequence sequence) {
				GetChildElementCompletionData (data, sequence.Items, prefix);
				return;
			}
			if (complexType.Particle is XmlSchemaChoice choice) {
				GetChildElementCompletionData (data, choice.Items, prefix);
				return;
			}
			var complexContent = complexType.ContentModel as XmlSchemaComplexContent;
			if (complexContent != null) {
				GetChildElementCompletionData (data, complexContent, prefix);
				return;
			}
			var groupRef = complexType.Particle as XmlSchemaGroupRef;
			if (groupRef != null) {
				GetChildElementCompletionData (data, groupRef, prefix);
				return;
			}
			var all = complexType.Particle as XmlSchemaAll;
			if (all != null) {
				GetChildElementCompletionData (data, all.Items, prefix);
				return;
			}
		}

		void GetChildElementCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaObjectCollection items, string prefix)
		{
			foreach (XmlSchemaObject schemaObject in items) {
				var childElement = schemaObject as XmlSchemaElement;
				if (childElement != null) {
					string name = childElement.Name;
					if (name == null) {
						name = childElement.RefName.Name;
						var element = FindElement (childElement.RefName);
						if (element != null) {
							if (element.IsAbstract) {
								AddSubstitionGroupElements (data, element.Name, prefix);
							} else {
								data.AddElement (name, prefix, element.Annotation);
							}
						} else {
							data.AddElement (name, prefix, childElement.Annotation);
						}
					} else {
						data.AddElement (name, prefix, childElement.Annotation);
					}
					continue;
				}
				var childSequence = schemaObject as XmlSchemaSequence;
				if (childSequence != null) {
					GetChildElementCompletionData (data, childSequence.Items, prefix);
					continue;
				}
				var childChoice = schemaObject as XmlSchemaChoice;
				if (childChoice != null) {
					GetChildElementCompletionData (data, childChoice.Items, prefix);
					continue;
				}
				var groupRef = schemaObject as XmlSchemaGroupRef;
				if (groupRef != null) {
					GetChildElementCompletionData (data, groupRef, prefix);
					continue;
				}
			}
		}

		void GetChildElementCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaComplexContent complexContent, string prefix)
		{
			var extension = complexContent.Content as XmlSchemaComplexContentExtension;
			if (extension != null) {
				GetChildElementCompletionData (data, extension, prefix);
				return;
			}
			var restriction = complexContent.Content as XmlSchemaComplexContentRestriction;
			if (restriction != null) {
				GetChildElementCompletionData (data, restriction, prefix);
				return;
			}
		}

		void GetChildElementCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaComplexContentExtension extension, string prefix)
		{
			var complexType = FindNamedType (schema, extension.BaseTypeName);
			if (complexType != null)
				GetChildElementCompletionData (data, complexType, prefix);

			if (extension.Particle == null)
				return;

			var sequence = extension.Particle as XmlSchemaSequence;
			if (sequence != null) {
				GetChildElementCompletionData (data, sequence.Items, prefix);
				return;
			}
			var choice = extension.Particle as XmlSchemaChoice;
			if (choice != null) {
				GetChildElementCompletionData (data, choice.Items, prefix);
				return;
			}
			var groupRef = extension.Particle as XmlSchemaGroupRef;
			if (groupRef != null) {
				GetChildElementCompletionData (data, groupRef, prefix);
				return;
			}
		}

		void GetChildElementCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaGroupRef groupRef, string prefix)
		{
			var group = FindGroup (groupRef.RefName.Name);
			if (group == null)
				return;
			var sequence = group.Particle as XmlSchemaSequence;
			if (sequence != null) {
				GetChildElementCompletionData (data, sequence.Items, prefix);
				return;
			}
			var choice = group.Particle as XmlSchemaChoice;
			if (choice != null) {
				GetChildElementCompletionData (data, choice.Items, prefix);
				return;
			}
		}

		void GetChildElementCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaComplexContentRestriction restriction, string prefix)
		{
			if (restriction.Particle == null)
				return;
			var sequence = restriction.Particle as XmlSchemaSequence;
			if (sequence != null) {
				GetChildElementCompletionData (data, sequence.Items, prefix);
				return;
			}
			var choice = restriction.Particle as XmlSchemaChoice;
			if (choice != null) {
				GetChildElementCompletionData (data, choice.Items, prefix);
				return;
			}
			var groupRef = restriction.Particle as XmlSchemaGroupRef;
			if (groupRef != null) {
				GetChildElementCompletionData (data, groupRef, prefix);
				return;
			}
		}
		#endregion

		#region FindMethods
		public static XmlSchemaComplexType FindNamedType (XmlSchema schema, XmlQualifiedName name)
		{

			if (name == null)
				return null;

			foreach (XmlSchemaComplexType complexType in schema.Items.OfType<XmlSchemaComplexType> ()) {
				if (complexType != null && complexType.Name == name.Name)
					return complexType;
			}

			// Try included schemas.
			foreach (XmlSchemaExternal external in schema.Includes) {
				var include = external as XmlSchemaInclude;
				if (include != null && include.Schema != null) {
					var matchedComplexType = FindNamedType (include.Schema, name);
					if (matchedComplexType != null)
						return matchedComplexType;
				}
			}

			return null;
		}

		public XmlSchemaElement FindElement (string name, XmlSchema schema = null)
		{
			if (this.schema == null) {
				return null;
			}

			if (schema == null) {
				schema = this.schema;
			}


			foreach (XmlSchemaElement element in schema.Items.OfType<XmlSchemaElement> ()) {
				//if (name.Equals (element.QualifiedName)) {
				//	return element;
				//}
				if (name == element.Name) {
					return element;
				}
			}

			// Try included schemas.
			foreach (XmlSchemaExternal external in schema.Includes) {
				var include = external as XmlSchemaInclude;
				if (include != null && include.Schema != null) {
					var matchedElement = FindElement (name, include.Schema);
					if (matchedElement != null)
						return matchedElement;
				}
			}

			LoggingService.LogDebug ("XmlSchemaDataObject did not find element '{0}' in the schema", name);
			return null;
		}

		/// <summary>
		/// Finds an element in the schema.
		/// </summary>
		/// <remarks>
		/// Only looks at the elements that are defined in the 
		/// root of the schema so it will not find any elements
		/// that are defined inside any complex types.
		/// </remarks>
		public XmlSchemaElement FindElement (XmlQualifiedName name, XmlSchema schema = null)
		{
			if (this.schema == null) {
				return null;
			}

			if (schema == null) {
				schema = this.schema;
			}

			foreach (XmlSchemaElement element in schema.Items.OfType<XmlSchemaElement>()) {
				// TODO: Figure out qualified names
				//if (name.Equals (element.QualifiedName)) {
				//	matchedElement = element;
				//	break;
				//}
				if (name.Name == element.Name) {
					//matchedElement = element;
					//break;
					return element;
				}
			}

			// Try included schemas.
			foreach (XmlSchemaExternal external in schema.Includes) {
				var include = external as XmlSchemaInclude;
				if (include != null && include.Schema != null) {
					var matchedElement = FindElement (name, include.Schema);
					if (matchedElement != null)
						return matchedElement;
				}
			}

			return null;
		}

		/// <summary>
		/// Finds an element in the schema.
		/// </summary>
		/// <remarks>
		/// Only looks at the elements that are defined in the 
		/// root of the schema so it will not find any elements
		/// that are defined inside any complex types.
		/// </remarks>
		public XmlSchemaElement FindElement (QualifiedName name, XmlSchema schema = null)
		{
			if(this.schema == null) {
				return null;
			}

			if (schema == null) {
				schema = this.schema;
			}


			foreach (XmlSchemaElement element in schema.Items.OfType<XmlSchemaElement> ()) {
				// TODO: FIgure out qualified names
				//if (name.Equals (element.QualifiedName)) {
				//	return element;
				//}
				if (name.Name == element.Name) {
					return element;
				}
			}

			// Try included schemas.
			foreach (XmlSchemaExternal external in schema.Includes) {
				var include = external as XmlSchemaInclude;
				if (include != null && include.Schema != null) {
					var matchedElement = FindElement (name, include.Schema);
					if (matchedElement != null)
						return matchedElement;
				}
			}

			LoggingService.LogDebug ("XmlSchemaDataObject did not find element '{0}' in the schema", name.Name);
			return null;
		}

		/// <summary>
		/// Finds the element that exists at the specified path.
		/// </summary>
		/// <remarks>This method is not used when generating completion data,
		/// but is a useful method when locating an element so we can jump
		/// to its schema definition.</remarks>
		/// <returns><see langword="null"/> if no element can be found.</returns>
		public XmlSchemaElement FindElement (XmlElementPath path)
		{
			XmlSchemaElement element = null;
			for (int i = 0; i < path.Elements.Count; ++i) {
				QualifiedName name = path.Elements[i];
				if (i == 0) {
					// Look for root element.
					element = FindElement (name);
					if (element == null) {
						break;
					}
				} else {
					element = FindChildElement (element, name);
					if (element == null) {
						break;
					}
				}
			}
			return element;
		}

		/// <summary>
		/// Finds the element in the collection of schema objects.
		/// </summary>
		XmlSchemaElement FindElement (XmlSchemaObjectCollection items, QualifiedName name)
		{
			XmlSchemaElement matchedElement = null;

			foreach (XmlSchemaObject schemaObject in items) {
				var element = schemaObject as XmlSchemaElement;
				var sequence = schemaObject as XmlSchemaSequence;
				var choice = schemaObject as XmlSchemaChoice;
				var groupRef = schemaObject as XmlSchemaGroupRef;

				if (element != null) {
					if (element.Name != null) {
						if (name.Name == element.Name) {
							return element;
						}
					} else if (element.RefName != null) {
						if (name.Name == element.RefName.Name) {
							matchedElement = FindElement (element.RefName);
						} else {
							var abstractElement = FindElement (element.RefName);
							if (abstractElement != null && abstractElement.IsAbstract) {
								matchedElement = FindSubstitutionGroupElement (abstractElement.Name, name.Name);
							}
						}
					}
				} else if (sequence != null) {
					matchedElement = FindElement (sequence.Items, name);
				} else if (choice != null) {
					matchedElement = FindElement (choice.Items, name);
				} else if (groupRef != null) {
					matchedElement = FindElement (groupRef, name);
				}

				if (matchedElement != null)
					return matchedElement;
			}

			return null;
		}

		XmlSchemaElement FindElement (XmlSchemaGroupRef groupRef, QualifiedName name)
		{
			var group = FindGroup (groupRef.RefName.Name);
			if (group == null)
				return null;

			var sequence = group.Particle as XmlSchemaSequence;
			if (sequence != null)
				return FindElement (sequence.Items, name);
			var choice = group.Particle as XmlSchemaChoice;
			if (choice != null)
				return FindElement (choice.Items, name);

			return null;
		}

		/// <summary>
		/// Finds an element that matches the specified <paramref name="name"/>
		/// from the children of the given <paramref name="element"/>.
		/// </summary>
		XmlSchemaElement FindChildElement (XmlSchemaElement element, QualifiedName name)
		{
			var complexType = GetElementAsComplexType (element);
			if (complexType != null)
				return FindChildElement (complexType, name);
			return null;
		}

		XmlSchemaElement FindChildElement (XmlSchemaComplexType complexType, QualifiedName name)
		{
			var sequence = complexType.Particle as XmlSchemaSequence;
			if (sequence != null)
				return FindElement (sequence.Items, name);

			var choice = complexType.Particle as XmlSchemaChoice;
			if (choice != null)
				return FindElement (choice.Items, name);

			var complexContent = complexType.ContentModel as XmlSchemaComplexContent;
			if (complexContent != null) {
				var extension = complexContent.Content as XmlSchemaComplexContentExtension;
				if (extension != null)
					return FindChildElement (extension, name);
				var restriction = complexContent.Content as XmlSchemaComplexContentRestriction;
				if (restriction != null)
					return FindChildElement (restriction, name);
			}

			var groupRef = complexType.Particle as XmlSchemaGroupRef;
			if (groupRef != null)
				return FindElement (groupRef, name);

			var all = complexType.Particle as XmlSchemaAll;
			if (all != null)
				return FindElement (all.Items, name);

			return null;
		}

		/// <summary>
		/// Finds the named child element contained in the extension element.
		/// </summary>
		XmlSchemaElement FindChildElement (XmlSchemaComplexContentExtension extension, QualifiedName name)
		{
			var complexType = FindNamedType (schema, extension.BaseTypeName);
			if (complexType == null)
				return null;

			var matchedElement = FindChildElement (complexType, name);
			if (matchedElement != null)
				return matchedElement;

			var sequence = extension.Particle as XmlSchemaSequence;
			if (sequence != null)
				return FindElement (sequence.Items, name);

			var choice = extension.Particle as XmlSchemaChoice;
			if (choice != null)
				return FindElement (choice.Items, name);

			var groupRef = extension.Particle as XmlSchemaGroupRef;
			if (groupRef != null)
				return FindElement (groupRef, name);

			return null;
		}

		/// <summary>
		/// Finds the named child element contained in the restriction element.
		/// </summary>
		XmlSchemaElement FindChildElement (XmlSchemaComplexContentRestriction restriction, QualifiedName name)
		{
			var sequence = restriction.Particle as XmlSchemaSequence;
			if (sequence != null)
				return FindElement (sequence.Items, name);

			var groupRef = restriction.Particle as XmlSchemaGroupRef;
			if (groupRef != null)
				return FindElement (groupRef, name);

			return null;
		}

		/// <summary>
		/// Finds the schema group with the specified name.
		/// </summary>
		public XmlSchemaGroup FindGroup (string name, XmlSchema schema = null)
		{
			// me
			if (schema == null) {
				schema = this.schema;
			}

			if (name != null) {
				foreach (XmlSchemaGroup group in schema.Items.OfType<XmlSchemaGroup> ()) {
					if (group != null && group.Name == name)
						return group;
				}
			}

			// Try included schemas.
			foreach (XmlSchemaExternal external in schema.Includes) {
				var include = external as XmlSchemaInclude;
				if (include != null && include.Schema != null) {
					var matchedGroup = FindGroup (name, include.Schema);
					if (matchedGroup != null)
						return matchedGroup;
				}
			}

			return null;
		}

		XmlSchemaSimpleType FindSimpleType (XmlQualifiedName name)
		{
			foreach (XmlSchemaObject schemaObject in schema.SchemaTypes.Values) {
				var simpleType = schemaObject as XmlSchemaSimpleType;
				if (simpleType != null && simpleType.QualifiedName == name)
					return simpleType;
			}
			return null;
		}

		/// <summary>
		/// Finds the simple type with the specified name.
		/// </summary>
		public XmlSchemaSimpleType FindSimpleType (string name)
		{
			var qualifiedName = new XmlQualifiedName (name, schema.TargetNamespace);
			return FindSimpleType (qualifiedName);
		}
		#endregion

		#region AttributeCompletion

		void GetAttributeCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaElement element)
		{
			var complexType = GetElementAsComplexType (element);
			if (complexType != null)
				GetAttributeCompletionData (data, complexType);
		}

		void GetAttributeCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaComplexContentRestriction restriction)
		{
			GetAttributeCompletionData (data, restriction.Attributes);

			var baseComplexType = FindNamedType (schema, restriction.BaseTypeName);
			if (baseComplexType != null) {
				GetAttributeCompletionData (data, baseComplexType);
			}
		}

		void GetAttributeCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaComplexType complexType)
		{
			GetAttributeCompletionData (data, complexType.Attributes);

			// Add any complex content attributes.
			var complexContent = complexType.ContentModel as XmlSchemaComplexContent;
			if (complexContent != null) {
				var extension = complexContent.Content as XmlSchemaComplexContentExtension;
				var restriction = complexContent.Content as XmlSchemaComplexContentRestriction;
				if (extension != null)
					GetAttributeCompletionData (data, extension);
				else if (restriction != null)
					GetAttributeCompletionData (data, restriction);
			} else {
				var simpleContent = complexType.ContentModel as XmlSchemaSimpleContent;
				if (simpleContent != null)
					GetAttributeCompletionData (data, simpleContent);
			}
		}

		void GetAttributeCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaComplexContentExtension extension)
		{
			GetAttributeCompletionData (data, extension.Attributes);
			var baseComplexType = FindNamedType (schema, extension.BaseTypeName);
			if (baseComplexType != null)
				GetAttributeCompletionData (data, baseComplexType);
		}

		void GetAttributeCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaSimpleContent simpleContent)
		{
			var extension = simpleContent.Content as XmlSchemaSimpleContentExtension;
			if (extension != null)
				GetAttributeCompletionData (data, extension);
		}

		void GetAttributeCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaSimpleContentExtension extension)
		{
			GetAttributeCompletionData (data, extension.Attributes);
		}

		void GetAttributeCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaObjectCollection attributes)
		{
			foreach (XmlSchemaObject schemaObject in attributes) {
				var attribute = schemaObject as XmlSchemaAttribute;
				if (attribute != null) {
					if (!IsProhibitedAttribute (attribute)) {
						data.AddAttribute (attribute);
					} else {
						prohibitedAttributes.Add (attribute);
					}
				} else {
					var attributeGroupRef = schemaObject as XmlSchemaAttributeGroupRef;
					if (attributeGroupRef != null)
						GetAttributeCompletionData (data, attributeGroupRef);
				}
			}
		}

		/// <summary>
		/// Checks that the attribute is prohibited or has been flagged
		/// as prohibited previously. 
		/// </summary>
		bool IsProhibitedAttribute (XmlSchemaAttribute attribute)
		{
			bool prohibited = false;
			if (attribute.Use == XmlSchemaUse.Prohibited) {
				prohibited = true;
			} else {
				foreach (XmlSchemaAttribute prohibitedAttribute in prohibitedAttributes) {
					if (prohibitedAttribute.QualifiedName == attribute.QualifiedName) {
						prohibited = true;
						break;
					}
				}
			}

			return prohibited;
		}

		/// <summary>
		/// Gets attribute completion data from a group ref.
		/// </summary>
		void GetAttributeCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaAttributeGroupRef groupRef)
		{
			var group = FindAttributeGroup (schema, groupRef.RefName.Name);
			if (group != null)
				GetAttributeCompletionData (data, group.Attributes);
		}

		#endregion

		#region AttributeValueCompletion

		static XmlSchemaAttributeGroup FindAttributeGroup (XmlSchema schema, string name)
		{
			if (name == null)
				return null;

			foreach (XmlSchemaObject schemaObject in schema.Items) {
				var group = schemaObject as XmlSchemaAttributeGroup;
				if (group != null && group.Name == name)
					return group;
			}

			// Try included schemas.
			foreach (XmlSchemaExternal external in schema.Includes) {
				var include = external as XmlSchemaInclude;
				if (include != null && include.Schema != null) {
					var found = FindAttributeGroup (include.Schema, name);
					if (found != null)
						return found;
				}
			}
			return null;
		}

		void GetAttributeValueCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaElement element, string name)
		{
			var complexType = GetElementAsComplexType (element);
			if (complexType != null) {
				var attribute = FindAttribute (complexType, name);
				if (attribute != null)
					GetAttributeValueCompletionData (data, attribute);
			}
		}

		void GetAttributeValueCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaAttribute attribute)
		{
			if (attribute.SchemaType != null) {
				var simpleTypeRestriction = attribute.SchemaType.Content as XmlSchemaSimpleTypeRestriction;
				if (simpleTypeRestriction != null) {
					GetAttributeValueCompletionData (data, simpleTypeRestriction);
				}
			} else if (attribute.AttributeSchemaType != null) {
				if (attribute.AttributeSchemaType.TypeCode == XmlTypeCode.Boolean)
					GetBooleanAttributeValueCompletionData (data);
				else
					GetAttributeValueCompletionData (data, attribute.AttributeSchemaType);
			}
		}

		void GetAttributeValueCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaSimpleTypeRestriction simpleTypeRestriction)
		{
			foreach (XmlSchemaObject schemaObject in simpleTypeRestriction.Facets) {
				var enumFacet = schemaObject as XmlSchemaEnumerationFacet;
				if (enumFacet != null)
					data.AddAttributeValue (enumFacet.Value, enumFacet.Annotation);
			}
		}

		void GetAttributeValueCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaSimpleTypeUnion union)
		{
			foreach (XmlSchemaObject schemaObject in union.BaseTypes) {
				var simpleType = schemaObject as XmlSchemaSimpleType;
				if (simpleType != null)
					GetAttributeValueCompletionData (data, simpleType);
			}
		}

		void GetAttributeValueCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaSimpleType simpleType)
		{
			var xsstr = simpleType.Content as XmlSchemaSimpleTypeRestriction;
			if (xsstr != null) {
				GetAttributeValueCompletionData (data, xsstr);
				return;
			}
			var xsstu = simpleType.Content as XmlSchemaSimpleTypeUnion;
			if (xsstu != null) {
				GetAttributeValueCompletionData (data, xsstu);
				return;
			}
			var xsstl = simpleType.Content as XmlSchemaSimpleTypeList;
			if (xsstl != null) {
				GetAttributeValueCompletionData (data, xsstl);
				return;
			}
		}

		void GetAttributeValueCompletionData (XmlSchemaCompletionBuilder data, XmlSchemaSimpleTypeList list)
		{
			if (list.ItemType != null) {
				GetAttributeValueCompletionData (data, list.ItemType);
			} else if (list.ItemTypeName != null) {
				var simpleType = FindSimpleType (list.ItemTypeName);
				if (simpleType != null)
					GetAttributeValueCompletionData (data, simpleType);
			}
		}

		/// <summary>
		/// Gets the set of attribute values for an xs:boolean type.
		/// </summary>
		void GetBooleanAttributeValueCompletionData (XmlSchemaCompletionBuilder data)
		{
			data.AddAttributeValue ("0");
			data.AddAttributeValue ("1");
			data.AddAttributeValue ("true");
			data.AddAttributeValue ("false");
		}

		XmlSchemaAttribute FindAttribute (XmlSchemaComplexType complexType, string name)
		{
			var matchedAttribute = FindAttribute (complexType.Attributes, name);
			if (matchedAttribute != null)
				return matchedAttribute;

			var complexContent = complexType.ContentModel as XmlSchemaComplexContent;
			if (complexContent != null)
				return FindAttribute (complexContent, name);

			return null;
		}

		XmlSchemaAttribute FindAttribute (XmlSchemaObjectCollection schemaObjects, string name)
		{
			foreach (XmlSchemaObject schemaObject in schemaObjects) {
				var attribute = schemaObject as XmlSchemaAttribute;
				if (attribute != null && attribute.Name == name)
					return attribute;

				var groupRef = schemaObject as XmlSchemaAttributeGroupRef;
				if (groupRef != null) {
					var matchedAttribute = FindAttribute (groupRef, name);
					if (matchedAttribute != null)
						return matchedAttribute;
				}
			}
			return null;
		}

		XmlSchemaAttribute FindAttribute (XmlSchemaAttributeGroupRef groupRef, string name)
		{
			if (groupRef.RefName != null) {
				var group = FindAttributeGroup (schema, groupRef.RefName.Name);
				if (group != null) {
					return FindAttribute (group.Attributes, name);
				}
			}
			return null;
		}

		XmlSchemaAttribute FindAttribute (XmlSchemaComplexContent complexContent, string name)
		{
			var extension = complexContent.Content as XmlSchemaComplexContentExtension;
			if (extension != null)
				return FindAttribute (extension, name);

			var restriction = complexContent.Content as XmlSchemaComplexContentRestriction;
			if (restriction != null)
				return FindAttribute (restriction, name);

			return null;
		}

		XmlSchemaAttribute FindAttribute (XmlSchemaComplexContentExtension extension, string name)
		{
			return FindAttribute (extension.Attributes, name);
		}

		XmlSchemaAttribute FindAttribute (XmlSchemaComplexContentRestriction restriction, string name)
		{
			var matchedAttribute = FindAttribute (restriction.Attributes, name);
			if (matchedAttribute != null)
				return matchedAttribute;

			var complexType = FindNamedType (schema, restriction.BaseTypeName);
			if (complexType != null)
				return FindAttribute (complexType, name);

			return null;
		}

		#endregion

		/// <summary>
		/// Adds any elements that have the specified substitution group.
		/// </summary>
		void AddSubstitionGroupElements (XmlSchemaCompletionBuilder data, string groupName, string prefix, XmlSchema schema = null)
		{
			//foreach (XmlSchemaElement element in schema.Elements.Values)
			//	if (element.SubstitutionGroup == group)
			//		data.AddElement (element.Name, prefix, element.Annotation);

			// me
			if (schema == null) {
				schema = this.schema;
			}


			foreach (XmlSchemaElement element in schema.Items.OfType<XmlSchemaElement> ()) {
				if (element.SubstitutionGroup.Name == groupName) {
					data.AddElement (element.Name, prefix, element.Annotation);
				}
			}

			// Try included schemas.
			foreach (XmlSchemaExternal external in schema.Includes) {
				var include = external as XmlSchemaInclude;
				if (include != null && include.Schema != null) {
					AddSubstitionGroupElements (data, groupName, prefix, include.Schema);
				}
			}
		}

		/// <summary>
		/// Looks for the substitution group element of the specified name.
		/// </summary>
		XmlSchemaElement FindSubstitutionGroupElement (string groupName, string name, XmlSchema schema = null)
		{
			//foreach (XmlSchemaElement element in schema.Elements.Values)
			//	if (element.SubstitutionGroup == group && element.Name != null && element.Name == name.Name)
			//		return element;

			// me
			if (schema == null) {
				schema = this.schema;
			}


			foreach (XmlSchemaElement element in schema.Items.OfType<XmlSchemaElement> ()) {
				if (element.SubstitutionGroup.Name == groupName && element.Name != null && element.Name == name) {
					return element;
				}
			}

			// Try included schemas.
			foreach (XmlSchemaExternal external in schema.Includes) {
				var include = external as XmlSchemaInclude;
				if (include != null && include.Schema != null) {
					var matchedElement = FindElement (name, include.Schema);
					if (matchedElement != null)
						return matchedElement;
				}
			}

			return null;
		}
	}
}
