using System;
using System.ComponentModel;
using System.Reflection;

namespace MachineJMDll.Utils
{
    /// <summary>
    /// ö�ٹ���
    /// </summary>
    public class EnumHelper
    {
        #region ����ö���ֶ�������ȡö��������
        /// <summary>
        /// ����ö���ֶ�������ȡö��������������EnumHelper.GetDescriptionByKey(typeof(Sex), "Man")
        /// </summary>
        public static string GetDescriptionByKey(Type T, string fieldName)
        {
            FieldInfo fieldInfo = T.GetField(fieldName);

            if (fieldInfo != null)
            {
                Object[] objArray = fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);

                if (objArray != null && objArray.Length > 0)
                {
                    DescriptionAttribute da = (DescriptionAttribute)objArray[0];
                    return da.Description;
                }
                else
                {
                    return fieldName;
                }
            }
            else
            {
                return "";
            }
        }
        #endregion

        #region ����ö��ֵ����ȡö��������
        /// <summary>
        /// ����ö��ֵ����ȡö��������������EnumHelper.GetDescriptionByVal(typeof(Sex), 1)
        /// </summary>
        public static string GetDescriptionByVal(Type T, int val)
        {
            return EnumHelper.GetDescriptionByKey(T, Enum.Parse(T, val.ToString()).ToString());
        }
        #endregion

    }
}
