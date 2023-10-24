#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Object = UnityEngine.Object;
#endif

using System;
using UnityEngine;

namespace Utility
{
    [AttributeUsage(AttributeTargets.Field)]
    internal class MultiShowIfAttribute : PropertyAttribute
    {
        public int Comparison { get; private set; }
        public string[] Conditions { get; private set; }

        public bool IsAnd { get; private set; }

        public MultiShowIfAttribute(bool isValue, params string[] conditions)
        {
            Comparison = isValue ? 1 : 0;
            Conditions = conditions;
            IsAnd = true;
        }

        public MultiShowIfAttribute(object objectValue, params string[] conditions)
        {
            Comparison = Convert.ToInt32(objectValue);
            Conditions = conditions;
            IsAnd = true;
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    internal class MultiEnableIfAttribute : PropertyAttribute
    {
        public int Comparison { get; private set; }
        public string[] Conditions { get; private set; }

        public bool IsAnd { get; private set; }

        public MultiEnableIfAttribute(bool isValue, params string[] conditions)
        {
            Comparison = isValue ? 1 : 0;
            Conditions = conditions;
            IsAnd = true;
        }

        public MultiEnableIfAttribute(object objectValue, params string[] conditions)
        {
            Comparison = Convert.ToInt32(objectValue);
            Conditions = conditions;
            IsAnd = true;
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(MultiShowIfAttribute), true)]
    [CustomPropertyDrawer(typeof(MultiEnableIfAttribute), true)]
    public class MultiShowIfAttributeDrawer : PropertyDrawer
    {
        #region Reflection helpers.

        private static MethodInfo GetMethod(object target, string methodName)
        {
            return GetAllMethods(target, m => m.Name.Equals(methodName,
                StringComparison.InvariantCulture)).FirstOrDefault();
        }

        private static FieldInfo GetField(object target, string fieldName)
        {
            return GetAllFields(target, f => f.Name.Equals(fieldName,
                StringComparison.InvariantCulture)).FirstOrDefault();
        }

        private static IEnumerable<FieldInfo> GetAllFields(object target, Func<FieldInfo,
            bool> predicate)
        {
            var types = new List<Type>()
            {
                target.GetType()
            };

            while (types.Last().BaseType != null)
            {
                types.Add(types.Last().BaseType);
            }

            for (var i = types.Count - 1; i >= 0; i--)
            {
                var fieldInfos = types[i]
                    .GetFields(BindingFlags.Instance | BindingFlags.Static |
                               BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(predicate);

                foreach (var fieldInfo in fieldInfos)
                {
                    yield return fieldInfo;
                }
            }
        }

        private static IEnumerable<MethodInfo> GetAllMethods(object target,
            Func<MethodInfo, bool> predicate)
        {
            var methodInfos = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.NonPublic | BindingFlags.Public)
                .Where(predicate);

            return methodInfos;
        }

        #endregion

        private bool MeetsConditions(SerializedProperty property)
        {
            var target = property.serializedObject.targetObject;
            int comparison;
            string[] conditions;
            bool isAnd;

            switch (attribute)
            {
                case MultiShowIfAttribute showIfAttribute:
                    comparison = showIfAttribute.Comparison;
                    conditions = showIfAttribute.Conditions;
                    isAnd = showIfAttribute.IsAnd;
                    break;
                case MultiEnableIfAttribute enableIfAttribute:
                    comparison = enableIfAttribute.Comparison;
                    conditions = enableIfAttribute.Conditions;
                    isAnd = enableIfAttribute.IsAnd;
                    break;
                default:
                    throw new InvalidOperationException();
            }

            return OpMeetsConditions(target, conditions, comparison, isAnd);
        }

        // ReSharper disable once CognitiveComplexity
        private bool OpMeetsConditions(Object target, string[] conditions, int comparison, bool isAnd)
        {
            var conditionValues = new List<bool>();

            foreach (var condition in conditions)
            {
                var conditionField = GetField(target, condition);
                var conditionMethod = GetMethod(target, condition);

                if (conditionField != null)
                {
                    if (conditionField.FieldType == typeof(bool))
                    {
                        var isValue = (bool)conditionField.GetValue(target);
                        conditionValues.Add(comparison == (isValue ? 1 : 0));
                    }
                    else
                    {
                        var enumValue = conditionField.GetValue(target);
                        conditionValues.Add(comparison == Convert.ToInt32(enumValue));
                    }
                }

                if (conditionMethod != null && conditionMethod.ReturnType == typeof(bool) &&
                    conditionMethod.GetParameters().Length == 0)
                {
                    conditionValues.Add((bool)conditionMethod.Invoke(target, null));
                }
            }

            if (conditionValues.Count > 0)
            {
                var met = isAnd
                    ? conditionValues.Aggregate(true, (current, value) => current && value)
                    : conditionValues.Aggregate(false, (current, value) => current || value);

                return met;
            }

            Debug.LogError("Invalid boolean condition fields or methods used!");
            return true;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent
            label)
        {
            // 속성 높이를 계산하고 조건을 충족하지 않고 그리기 모드가 DontDraw이면 높이는 0이됩니다.
            bool meetsCondition = MeetsConditions(property);
            var showIfAttribute = attribute is MultiShowIfAttribute;

            if (!meetsCondition && showIfAttribute)
                return 0;
            return base.GetPropertyHeight(property, label);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent
            label)
        {
            var meetsCondition = MeetsConditions(property);
            // Early out, if conditions met, draw and go.
            if (meetsCondition)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            var showIfAttribute = attribute is MultiShowIfAttribute;
            if (!showIfAttribute)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.PropertyField(position, property, label, true);
                EditorGUI.EndDisabledGroup();
            }
        }
    }
#endif
}