﻿using MachineJMDll.Enums;
using MachineJMDll.Utils;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Text;
using MachineJMDll.Models;

namespace MachineJMDll
{
    /// <summary>
    /// 售货机接口(金码接口)
    /// </summary>
    public class MachineJM
    {
        #region 变量与构造函数
        private static object _lock = new object();
        /// <summary>
        /// 串口资源
        /// </summary>
        private SerialPort serialPort = null;

        public MachineJM(string com)
        {
            serialPort = new SerialPort(com, 9600, Parity.None, 8, StopBits.One);
            serialPort.ReadBufferSize = 1024;
            serialPort.WriteBufferSize = 1024;
            serialPort.Open();
        }
        #endregion

        #region 向串口发送数据，读取返回数据
        /// <summary>
        /// 向串口发送数据，读取返回数据
        /// </summary>
        /// <param name="sendData">发送的数据</param>
        /// <returns>返回的数据</returns>
        private byte[] ReadPort(byte[] sendData)
        {
            lock (_lock)
            {
                //打开连接
                //if (!serialPort.IsOpen) serialPort.Open();

                //发送数据
                serialPort.Write(sendData, 0, sendData.Length);

                //读取返回数据
                DateTime dt = DateTime.Now;
                while (serialPort.BytesToRead < 2)
                {
                    Thread.Sleep(1);

                    if (DateTime.Now.Subtract(dt).TotalMilliseconds > 5000) //如果5秒后仍然无数据返回，则视为超时
                    {
                        throw new Exception("主版无响应");
                    }
                }
                List<byte> recList = new List<byte>();
                byte[] recData = new byte[serialPort.BytesToRead];
                serialPort.Read(recData, 0, recData.Length);
                recList.AddRange(recData);
                int length = recData[1] + 3; //报文数据总长度
                while (recList.Count < length)
                {
                    if (serialPort.BytesToRead > 0)
                    {
                        recData = new byte[serialPort.BytesToRead];
                        serialPort.Read(recData, 0, recData.Length);
                        recList.AddRange(recData);
                    }
                    Thread.Sleep(1);
                }

                //关闭连接
                //if (serialPort.IsOpen) serialPort.Close();

                return recList.ToArray();
            }
        }
        #endregion

        #region 根据返回的数据判断主版是否正在执行出货、退币、电机测试操作
        /// <summary>
        /// 根据返回的数据判断主版是否正在执行出货、退币、电机测试操作
        /// </summary>
        /// <param name="recData">接收到的数据</param>
        /// <param name="msg">传出错误信息</param>
        /// <returns>主版是否正在执行出货、退币、电机测试操作</returns>
        private bool IsRunning(byte[] recData, out string msg)
        {
            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x03
                && recData[2] == 0x05
                && CommonUtil.ValidCheckCode(recData)) //正在执行
            {
                switch (recData[4]) //判断执行情况
                {
                    case 0x00:
                        msg = "没有要执行的指令";
                        return true;
                    case 0xFF: //正在执行，未结束
                        switch (recData[3]) //判断执行类型
                        {
                            case 0x03:
                                msg = "正在执行出货";
                                return true;
                            case 0x04:
                                msg = "正在执行退币";
                                return true;
                            case 0x06:
                                msg = "正在执行电机测试";
                                return true;
                            default:
                                throw new Exception("未知状态");
                        }
                    default:
                        throw new Exception("未知状态");
                }
            }

            msg = "主版没有正在执行的操作";
            return false;
        }
        #endregion

        #region 根据返回的数据判断是否已联机
        /// <summary>
        /// 根据返回的数据判断是否已联机
        /// </summary>
        /// <param name="recData">接收到的数据</param>
        /// <param name="msg">传出错误信息</param>
        /// <returns>是否已联机</returns>
        private bool IsConnected(byte[] recData, out string msg)
        {
            if (recData.Length >= 4
               && recData[0] == 0x01
               && recData[1] == 0x02
               && recData[2] == 0x00
               && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "控制主板正在重启";
                        return false;
                    case 0x01:
                        msg = "联机成功";
                        return true;
                    case 0x02:
                        msg = "控制主板正在维护";
                        return false;
                    case 0x03:
                        msg = "控制主板收到的数据格式不正确";
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }

            msg = "未知状态(控制主板尚未启动？)";
            return false;
        }
        #endregion

        #region 联机
        /// <summary>
        /// 联机
        /// </summary>
        /// <param name="msg">传出错误信息</param>
        /// <returns>联机是否成功</returns>
        public bool Connect(out string msg)
        {
            byte[] sendData = new byte[] { 0x01, 0x01, 0x00, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x02
                && recData[2] == 0x00
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "控制主板正在重启";
                        return false;
                    case 0x01:
                        msg = "联机成功";
                        return true;
                    case 0x02:
                        msg = "控制主板正在维护";
                        return false;
                    case 0x03:
                        msg = "控制主板收到的数据格式不正确";
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 出货
        /// <summary>
        /// 出货
        /// </summary>
        /// <param name="boxNo">机柜编号，1、2、3…</param>
        /// <param name="floor">货道层</param>
        /// <param name="num">货道号</param>
        /// <param name="cashPay">是否现金支付，true现金支付，false非现金支付</param>
        /// <param name="cost">货道价格(单位：分)</param>
        /// <param name="checkDrop">是否检测掉货</param>
        /// <param name="msg">传出错误信息</param>
        /// <returns>是否正常出货</returns>
        public bool Shipment(int boxNo, int floor, int num, bool cashPay, int cost, bool checkDrop, out string msg)
        {
            byte roadNo = CommonUtil.CreateRoadNo(floor, num);
            byte cmdCashPay = cashPay ? (byte)0x00 : (byte)0x01;
            byte[] cmdCost = CommonUtil.Int2ByteArray(cost, 4);
            byte cmdCheckDrop = checkDrop ? (byte)0x01 : (byte)0x00;
            List<byte> sendData = new List<byte>() { 0x01, 0x09, 0x03, (byte)boxNo, roadNo, cmdCashPay };
            sendData.AddRange(cmdCost);
            sendData.AddRange(new byte[] { cmdCheckDrop, 0x00 });
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x05
                && recData[2] == 0x03
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[5])
                {
                    case 0x00:
                        msg = "货道正常，正在执行出货动作";
                        return true;
                    case 0x01:
                        msg = "货道故障：" + EnumHelper.GetDescriptionByVal(typeof(RoadStatus), recData[6]);
                        return false;
                    case 0x02:
                        msg = "控制主板中的可用金额小于货道价格，不能出货";
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 硬币器使能
        /// <summary>
        /// 硬币器使能
        /// </summary>
        /// <returns>硬币器使能成功或失败</returns>
        public bool CoinEnable(out string msg)
        {
            byte[] sendData = new byte[] { 0x01, 0x02, 0x0A, 0x01, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x03
                && recData[2] == 0x0A
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "成功";
                        return true;
                    case 0x01:
                        msg = "硬币器故障：" + EnumHelper.GetDescriptionByVal(typeof(CoinStatus), recData[4]);
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 硬币器禁能
        /// <summary>
        /// 硬币器禁能
        /// </summary>
        /// <returns>硬币器禁能成功或失败</returns>
        public bool CoinDisable(out string msg)
        {
            byte[] sendData = new byte[] { 0x01, 0x02, 0x0A, 0x00, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x03
                && recData[2] == 0x0A
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "成功";
                        return true;
                    case 0x01:
                        msg = "硬币器故障：" + EnumHelper.GetDescriptionByVal(typeof(CoinStatus), recData[4]);
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 纸币器使能
        /// <summary>
        /// 纸币器使能
        /// </summary>
        /// <returns>纸币器使能成功或失败</returns>
        public bool PaperMoneyEnable(out string msg)
        {
            byte[] sendData = new byte[] { 0x01, 0x02, 0x07, 0x02, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x03
                && recData[2] == 0x07
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "成功";
                        return true;
                    case 0x01:
                        msg = "纸币器故障：" + EnumHelper.GetDescriptionByVal(typeof(PaperMoneyStatus), recData[4]);
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 纸币器禁能
        /// <summary>
        /// 纸币器禁能
        /// </summary>
        /// <returns>纸币器禁能成功或失败</returns>
        public bool PaperMoneyDisable(out string msg)
        {
            byte[] sendData = new byte[] { 0x01, 0x02, 0x07, 0x00, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x03
                && recData[2] == 0x07
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "成功";
                        return true;
                    case 0x01:
                        msg = "纸币器故障：" + EnumHelper.GetDescriptionByVal(typeof(PaperMoneyStatus), recData[4]);
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 查询金额
        /// /// <summary>
        /// 查询金额(单位：分)
        /// </summary>
        /// <param name="type">货币类型(0：硬币，1：纸币)</param>
        /// <returns>金额数(单位：分)</returns>
        public int QueryAmount(out int type)
        {
            string msg = null;
            byte[] sendData = new byte[] { 0x01, 0x01, 0x02, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x09
                && recData[2] == 0x02
                && CommonUtil.ValidCheckCode(recData))
            {
                type = (int)recData[6];
                return CommonUtil.ByteArray2Int(new byte[] { recData[7], recData[8], recData[9], recData[10] });
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                throw new Exception(msg);
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 同步金额
        /// <summary>
        /// 同步金额(单位：分)
        /// </summary>
        /// <returns>金额数</returns>
        public int SyncAmount()
        {
            string msg = null;
            byte[] sendData = new byte[] { 0x01, 0x02, 0x0C, 0x00, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x05
                && recData[2] == 0x0C
                && CommonUtil.ValidCheckCode(recData))
            {
                return CommonUtil.ByteArray2Int(new byte[] { recData[3], recData[4], recData[5], recData[6] });
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                throw new Exception(msg);
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 清除金额
        /// <summary>
        /// 清除金额(单位：分)
        /// </summary>
        /// <returns>控制主板上现在的可用金额（完成清除后返回0）</returns>
        public int ClearAmount()
        {
            string msg = null;
            byte[] sendData = new byte[] { 0x01, 0x02, 0x0C, 0x01, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x05
                && recData[2] == 0x0C
                && CommonUtil.ValidCheckCode(recData))
            {
                return CommonUtil.ByteArray2Int(new byte[] { recData[3], recData[4], recData[5], recData[6] });
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                throw new Exception(msg);
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 查询出货结果
        /// <summary>
        /// 查询出货结果
        /// </summary>
        /// <param name="isSuccess">出货是否成功</param>
        /// <param name="remainder">余额(单位:分)</param>
        /// <param name="checkDrop">是否检测掉货</param>
        /// <returns>出货是否已完成</returns>
        public bool QueryShipment(out bool isSuccess, out int remainder, bool checkDrop, out string msg)
        {
            byte[] sendData = new byte[] { 0x01, 0x01, 0x05, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x09
                && recData[2] == 0x05
                && CommonUtil.ValidCheckCode(recData)) //执行结束
            {
                if (recData[4] == 0x00) //出货成功
                {
                    remainder = CommonUtil.ByteArray2Int(new byte[] { recData[6], recData[7], recData[8], recData[9] });

                    if (checkDrop) //检测掉货
                    {
                        msg = "掉货检测结果：";
                        isSuccess = false;
                        switch (recData[10])
                        {
                            #region case
                            case 0x00:
                                msg += "掉货检测未连接";
                                break;
                            case 0x01:
                                msg += "掉货检测检测到货物";
                                isSuccess = true;
                                break;
                            case 0x02:
                                msg += "掉货检测没有检测到货物";
                                break;
                            case 0x03:
                                msg += "掉货检测故障";
                                break;
                            case 0x04:
                                msg += "掉货检测被遮挡";
                                break;
                            case 0x05:
                                msg += "掉货检测失去连接";
                                break;
                            default:
                                msg += "未知状态";
                                break;
                            #endregion
                        }
                    }
                    else //不检测掉货
                    {
                        msg = "出货成功";
                        isSuccess = true;
                    }
                }
                else //出货失败
                {
                    msg = "货道状态：";
                    switch (recData[5])
                    {
                        #region case
                        case 0x01:
                            msg += "故障";
                            break;
                        case 0x02:
                            msg += "正常";
                            break;
                        case 0x03:
                            msg += "线路不通";
                            break;
                        case 0x04:
                            msg += "未安装电机（电机转接板未安装）";
                            break;
                        case 0x05:
                            msg += "电机在4秒时限内不能压下微动开关到达相应位置";
                            break;
                        case 0x06:
                            msg += "电机在4秒时限内不能归位到达相应位置，无掉货检测时可扣费";
                            break;
                        case 0x07:
                            msg += "驱动IC出错";
                            break;
                        case 0x08:
                            msg += "电机不在正确位置（电机不在位）";
                            break;
                        case 0x09:
                            msg += "电机卡塞";
                            break;
                        case 0x0E:
                            msg += "电机电流超限（电机过流）";
                            break;
                        default:
                            msg += "未知状态";
                            break;
                        #endregion
                    }

                    remainder = -1;
                    isSuccess = false;
                }

                return true;
            }

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x03
                && recData[2] == 0x05
                && CommonUtil.ValidCheckCode(recData)) //正在执行
            {
                msg = "正在出货";
                isSuccess = false;
                remainder = -1;
                return false;
            }

            throw new Exception("货机返回的数据格式不正确");
        }
        #endregion

        #region 查询可找硬币金额
        /// <summary>
        /// 查询可找硬币金额(单位：分)，返回-1表示查询失败
        /// </summary>
        /// <returns>金额(单位：分)</returns>
        public int QueryGiveChange(out string msg)
        {
            byte[] sendData = new byte[] { 0x01, 0x01, 0x0B, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x07
                && recData[2] == 0x0B
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "查询可找硬币金额成功";
                        return CommonUtil.ByteArray2Int(new byte[] { recData[5], recData[6], recData[7], recData[8] });
                    case 0x01:
                        msg = "硬币器故障：";
                        switch (recData[4])
                        {
                            #region case
                            case 0x01:
                                msg += "故障";
                                break;
                            case 0x02:
                                msg += "正常";
                                break;
                            case 0x03:
                                msg += "退币按钮（指售货机设备上的硬件退币按钮）被触发";
                                break;
                            case 0x05:
                                msg += "识别硬币面值失败";
                                break;
                            case 0x06:
                                msg += "检测到币桶感应器异常";
                                break;
                            case 0x07:
                                msg += "两个硬币一起被接受";
                                break;
                            case 0x08:
                                msg += "找不到硬币识别头";
                                break;
                            case 0x09:
                                msg += "一个储币管卡塞";
                                break;
                            case 0x0A:
                                msg += "ROM校验和错误";
                                break;
                            case 0x0B:
                                msg += "硬币接收路径错误";
                                break;
                            case 0x0E:
                                msg += "接收通道有硬币卡塞";
                                break;
                            case 0xFA:
                                msg += "硬币盒已满";
                                break;
                            case 0xFB:
                                msg += "硬币盒被取走";
                                break;
                            case 0xFC:
                                msg += "可找零";
                                break;
                            case 0xFD:
                                msg += "零钱不足";
                                break;
                            case 0xFF:
                                msg += "和硬币器断开连接";
                                break;
                            default:
                                msg += "未知状态";
                                break;
                            #endregion

                        }
                        return -1;
                    default:
                        msg = "未知状态";
                        return -1;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return -1;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 退币
        /// <summary>
        /// 退币
        /// </summary>
        /// <param name="amount">要退出的金额(单位：分)</param>
        /// <param name="msg">传出错误信息</param>
        /// <returns>是否正常退币</returns>
        public bool RefundMoney(int amount, out string msg)
        {
            if (amount == 0)
            {
                msg = "没有可退的金额";
                return false;
            }

            byte[] cmdAmount = CommonUtil.Int2ByteArray(amount, 4);
            List<byte> sendData = new List<byte>() { 0x01, 0x05, 0x04 };
            sendData.AddRange(cmdAmount);
            sendData.AddRange(new byte[] { 0x00 });
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x03
                && recData[2] == 0x04
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "状态正常，正在执行退币";
                        return true;
                    case 0x01:
                        msg = "硬币器故障：";
                        switch (recData[4])
                        {
                            #region case
                            case 0x01:
                                msg += "故障";
                                break;
                            case 0x02:
                                msg += "正常";
                                break;
                            case 0x03:
                                msg += "退币按钮（指售货机设备上的硬件退币按钮）被触发";
                                break;
                            case 0x05:
                                msg += "识别硬币面值失败";
                                break;
                            case 0x06:
                                msg += "检测到币桶感应器异常";
                                break;
                            case 0x07:
                                msg += "两个硬币一起被接受";
                                break;
                            case 0x08:
                                msg += "找不到硬币识别头";
                                break;
                            case 0x09:
                                msg += "一个储币管卡塞";
                                break;
                            case 0x0A:
                                msg += "ROM校验和错误";
                                break;
                            case 0x0B:
                                msg += "硬币接收路径错误";
                                break;
                            case 0x0E:
                                msg += "接收通道有硬币卡塞";
                                break;
                            case 0xFA:
                                msg += "硬币盒已满";
                                break;
                            case 0xFB:
                                msg += "硬币盒被取走";
                                break;
                            case 0xFC:
                                msg += "可找零";
                                break;
                            case 0xFD:
                                msg += "零钱不足";
                                break;
                            case 0xFF:
                                msg += "和硬币器断开连接";
                                break;
                            default:
                                msg += "未知状态";
                                break;
                            #endregion

                        }
                        return false;
                    case 0x02:
                        msg = "硬币器中无币可找";
                        return false;
                    case 0x03:
                        msg = "控制主板的可用金额已小于退币金额";
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 查询退币结果
        /// <summary>
        /// 查询退币结果
        /// </summary>
        /// <param name="isSuccess">退币是否成功</param>
        /// <param name="remainder">余额(单位:分)</param>
        /// <param name="msg">传出错误信息</param>
        /// <returns>退币是否已完成</returns>
        public bool QueryRefundMoney(out bool isSuccess, out int remainder, out string msg)
        {
            byte[] sendData = new byte[] { 0x01, 0x01, 0x05, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x08
                && recData[2] == 0x05
                && CommonUtil.ValidCheckCode(recData)) //执行结束
            {
                remainder = CommonUtil.ByteArray2Int(new byte[] { recData[6], recData[7], recData[8], recData[9] });

                isSuccess = false;
                switch (recData[4])
                {
                    case 0x00:
                        msg = "退币成功";
                        isSuccess = true;
                        break;
                    case 0x01:
                        msg = "硬币器故障：" + EnumHelper.GetDescriptionByVal(typeof(CoinStatus), recData[5]);
                        break;
                    case 0x02:
                        msg = "硬币器中已无币可找";
                        break;
                    case 0x03:
                        msg = "控制主板的可用金额已小于退币金额";
                        break;
                    default:
                        msg = "未知状态";
                        break;
                }

                return true;
            }

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x03
                && recData[2] == 0x05
                && CommonUtil.ValidCheckCode(recData)) //正在执行
            {
                msg = "正在退币";
                isSuccess = false;
                remainder = -1;
                return false;
            }

            throw new Exception("货机返回的数据格式不正确");
        }
        #endregion

        #region 查询控制主板信息
        /// <summary>
        /// 查询控制主板信息
        /// </summary>
        public MainBoardInfo QueryMainBoardInfo()
        {
            string msg = null;
            byte[] sendData = new byte[] { 0x01, 0x01, 0x20, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x1F
                && recData[2] == 0x20
                && CommonUtil.ValidCheckCode(recData))
            {
                MainBoardInfo mainBoardInfo = new Models.MainBoardInfo();

                //控制主板协议版本号
                StringBuilder sbVersion = new StringBuilder();
                for (int i = 3; i <= 4; i++)
                {
                    int v;
                    if (int.TryParse(recData[i].ToString("x2"), out v))
                    {
                        sbVersion.Append(v.ToString());
                    }
                    else
                    {
                        throw new Exception("获取控制主板协议版本号出错");
                    }
                    if (i == 3) sbVersion.Append(".");
                }
                mainBoardInfo.Version = sbVersion.ToString();

                //控制主版时间
                StringBuilder sbTime = new StringBuilder();
                for (int i = 5; i <= 11; i++)
                {
                    sbTime.Append(recData[i].ToString("x2"));
                }
                mainBoardInfo.Time = sbTime.ToString();

                //控制主板序列号
                StringBuilder sbSerialNumber = new StringBuilder();
                for (int i = 12; i <= 21; i++)
                {
                    sbSerialNumber.Append(recData[i].ToString("x2").Substring(1, 1));
                }
                mainBoardInfo.SerialNumber = sbSerialNumber.ToString();

                //控制主板软件版本号
                StringBuilder sbSoftVersion = new StringBuilder();
                for (int i = 22; i <= 32; i++)
                {
                    sbSoftVersion.Append((char)recData[i]);
                }
                mainBoardInfo.SoftVersion = sbSoftVersion.ToString();

                return mainBoardInfo;
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                throw new Exception(msg);
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 设置控制主板时间
        /// <summary>
        /// 设置控制主板时间
        /// </summary>
        /// <param name="msg">传出错误信息</param>
        /// <returns>是否设置成功</returns>
        public bool SetMainBoardTime(out string msg)
        {
            byte[] cmdTime = new byte[7];
            for (int i = 0; i < 7; i++)
            {
                int val = Convert.ToInt32(DateTime.Now.ToString("yyyyMMddHHmmss").Substring(i * 2, 2), 16);
                cmdTime[i] = (byte)val;
            }
            List<byte> sendData = new List<byte>() { 0x01, 0x08, 0x30 };
            sendData.AddRange(cmdTime);
            sendData.AddRange(new byte[] { 0x00 });
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x02
                && recData[2] == 0x30
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "设置成功";
                        return true;
                    case 0x01:
                        msg = "设置失败";
                        return false;
                    case 0x02:
                        msg = "时间格式错误";
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 查询货道信息
        /// <summary>
        /// 查询货道信息
        /// </summary>
        /// <param name="boxNo">机柜编号</param>
        /// <param name="floor">货道层</param>
        /// <param name="num">货道号</param>
        /// <param name="cost">货道价格(单位：分)</param>
        /// <param name="status">货道状态</param>
        /// <returns>是否正常</returns>
        public bool QueryRoadInfo(int boxNo, int floor, int num, out int cost, out string status)
        {
            string msg = null;
            byte roadNo = CommonUtil.CreateRoadNo(floor, num);
            List<byte> sendData = new List<byte>() { 0x01, 0x03, 0x01, (byte)boxNo, roadNo, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x08
                && recData[2] == 0x01
                && CommonUtil.ValidCheckCode(recData))
            {
                cost = CommonUtil.ByteArray2Int(new byte[] { recData[6], recData[7], recData[8], recData[9] });
                status = EnumHelper.GetDescriptionByVal(typeof(RoadStatus), recData[5]);
                if (recData[5] == 2)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                throw new Exception(msg);
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 查询货道信息
        /// <summary>
        /// 查询货道信息
        /// </summary>
        /// <param name="boxNo">机柜编号</param>
        /// <param name="floor">货道层</param>
        /// <param name="num">货道号</param>
        /// <returns>货道状态</returns>
        public RoadInfo QueryRoadInfo(int boxNo, int floor, int num)
        {
            string msg = null;
            RoadInfo roadInfo = new RoadInfo();
            roadInfo.Floor = floor;
            roadInfo.Num = num;
            byte roadNo = CommonUtil.CreateRoadNo(floor, num);
            List<byte> sendData = new List<byte>() { 0x01, 0x03, 0x01, (byte)boxNo, roadNo, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x08
                && recData[2] == 0x01
                && CommonUtil.ValidCheckCode(recData))
            {
                string roadStatus = EnumHelper.GetDescriptionByVal(typeof(RoadStatus), recData[5]);
                if (recData[5] == 2)
                {
                    roadInfo.IsOK = true;
                }
                else
                {
                    roadInfo.IsOK = false;
                }
                roadInfo.ErrorMsg = roadStatus;

                if (recData[5] == 2)
                {
                    int roadCost = CommonUtil.ByteArray2Int(new byte[] { recData[6], recData[7], recData[8], recData[9] });
                    roadInfo.Price = roadCost;
                }

                return roadInfo;
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                throw new Exception("货机正在执行操作，请稍后检测");
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 查询货道信息
        /// <summary>
        /// 查询货道信息
        /// </summary>
        /// <param name="boxNo">机柜编号</param>
        /// <param name="roadNoList">货道编号集合，货道编号例：B12</param>
        /// <returns>货道集合的状态集合</returns>
        public List<RoadInfo> QueryRoadInfo(int boxNo, List<string> roadNoList)
        {
            List<RoadInfo> result = new List<RoadInfo>();
            int floor = 0;
            int num = 0;

            foreach (string roadNo in roadNoList)
            {
                try
                {
                    floor = CommonUtil.ABCDToNum(roadNo[0]);
                    num = int.Parse(roadNo.Substring(1));
                }
                catch
                {
                    throw new Exception("货道编号格式不正确");
                }

                RoadInfo roadInfo = QueryRoadInfo(boxNo, floor, num);
                result.Add(roadInfo);
            }

            return result;
        }
        #endregion

        #region 货道电机测试
        /// <summary>
        /// 货道电机测试
        /// </summary>
        /// <param name="boxNo">机柜编号，1、2、3…</param>
        /// <param name="floor">货道层</param>
        /// <param name="num">货道号</param>
        /// <param name="msg">传出错误信息</param>
        /// <returns>是否正常</returns>
        public bool TestRoad(int boxNo, int floor, int num, out string msg)
        {
            byte roadNo = CommonUtil.CreateRoadNo(floor, num);
            List<byte> sendData = new List<byte>() { 0x01, 0x03, 0x06, (byte)boxNo, roadNo, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x05
                && recData[2] == 0x06
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[5])
                {
                    case 0x00:
                        msg = "货道正常，正在执行动作";
                        return true;
                    case 0x01:
                        msg = "货道故障：" + EnumHelper.GetDescriptionByVal(typeof(RoadStatus), recData[6]);
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 查询货道电机测试
        /// <summary>
        /// 查询货道电机测试
        /// </summary>
        /// <param name="isSuccess">测试是否成功</param>
        /// <param name="msg">传出错误信息</param>
        /// <returns>测试是否已完成</returns>
        public bool QueryTestRoad(out bool isSuccess, out string msg)
        {
            byte[] sendData = new byte[] { 0x01, 0x01, 0x05, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData);

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x04
                && recData[2] == 0x05
                && CommonUtil.ValidCheckCode(recData)) //执行结束
            {
                isSuccess = false;
                switch (recData[4])
                {
                    case 0x00:
                        msg = "操作成功";
                        isSuccess = true;
                        break;
                    case 0x01:
                        msg = "操作失败：" + EnumHelper.GetDescriptionByVal(typeof(RoadStatus), recData[5]);
                        break;
                    default:
                        msg = "未知状态";
                        break;
                }

                return true;
            }

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x03
                && recData[2] == 0x05
                && CommonUtil.ValidCheckCode(recData)) //正在执行
            {
                msg = "正在执行货道电机测试操作";
                isSuccess = false;
                return false;
            }

            throw new Exception("货机返回的数据格式不正确");
        }
        #endregion

        #region 查询机器设备状态
        /// <summary>
        /// 查询机器设备状态
        /// </summary>
        /// <param name="boxNo">机柜编号</param>
        /// <returns>机器状态集合</returns>
        public BoxStatus QueryBoxStatus(int boxNo)
        {
            string msg = null;

            BoxStatus boxStatus = new BoxStatus();
            List<byte> sendData = new List<byte>() { 0x01, 0x02, 0x10, (byte)boxNo, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x0B
                && recData[2] == 0x10
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        #region 驱动板状态
                        switch (recData[4])
                        {
                            case 0x01:
                                boxStatus.驱动板状态.IsOK = false;
                                boxStatus.驱动板状态.Msg = "故障(驱动板没有连接)";
                                break;
                            case 0x02:
                                boxStatus.驱动板状态.IsOK = true;
                                boxStatus.驱动板状态.Msg = "正常";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 温度传感器状态
                        switch (recData[5])
                        {
                            case 0x00:
                                boxStatus.温度传感器状态.IsOK = true;
                                boxStatus.温度传感器状态.Msg = "检测到温度值，温度值有效";
                                break;
                            case 0x01:
                                boxStatus.温度传感器状态.IsOK = false;
                                boxStatus.温度传感器状态.Msg = "温度检测超范围，温度值无效";
                                break;
                            case 0x02:
                                boxStatus.温度传感器状态.IsOK = false;
                                boxStatus.温度传感器状态.Msg = "线路不通或未接温度传感器，温度值无效";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 当前温度值
                        boxStatus.当前温度值.IsOK = true;
                        boxStatus.当前温度值.Msg = (recData[6] >= 128 ? -recData[6] % 128 : recData[6]).ToString() + "℃";
                        #endregion

                        #region 目标温度值
                        boxStatus.目标温度值.IsOK = true;
                        boxStatus.目标温度值.Msg = (recData[7] % 128).ToString() + "℃";
                        #endregion

                        #region 门碰开关状态
                        switch (recData[8])
                        {
                            case 0x00:
                                boxStatus.门碰开关状态.IsOK = false;
                                boxStatus.门碰开关状态.Msg = "门控开关合上，表示关门";
                                break;
                            case 0x01:
                                boxStatus.门碰开关状态.IsOK = true;
                                boxStatus.门碰开关状态.Msg = "门控开关打开，表示开门";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 掉货检测设备状态
                        switch (recData[9])
                        {
                            case 0x01:
                                boxStatus.掉货检测设备状态.IsOK = false;
                                boxStatus.掉货检测设备状态.Msg = "故障";
                                break;
                            case 0x02:
                                boxStatus.掉货检测设备状态.IsOK = true;
                                boxStatus.掉货检测设备状态.Msg = "正常，中间没隔断";
                                break;
                            case 0x03:
                                boxStatus.掉货检测设备状态.IsOK = false;
                                boxStatus.掉货检测设备状态.Msg = "被遮挡（有隔断）";
                                break;
                            case 0xFF:
                                boxStatus.掉货检测设备状态.IsOK = false;
                                boxStatus.掉货检测设备状态.Msg = "设备没安装（无连接）";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 网络设备状态
                        switch (recData[10])
                        {
                            case 0x00:
                                boxStatus.网络设备状态.IsOK = false;
                                boxStatus.网络设备状态.Msg = "离线";
                                break;
                            case 0x01:
                                boxStatus.网络设备状态.IsOK = true;
                                boxStatus.网络设备状态.Msg = "联机";
                                break;
                            case 0xFF:
                                boxStatus.网络设备状态.IsOK = false;
                                boxStatus.网络设备状态.Msg = "不存在网络设备或不具有网络功能";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 网络待发数据数量
                        boxStatus.网络待发数据数量.IsOK = true;
                        boxStatus.网络待发数据数量.Msg = CommonUtil.ByteArray2Int(new byte[] { recData[11], recData[12] }).ToString();
                        #endregion

                        return boxStatus;
                    case 0x01:
                        throw new Exception("不存在该机柜编号");
                    default:
                        throw new Exception("未知状态");
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                throw new Exception("货机正在执行操作，请稍后检测");
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 查询制冷压缩机/风机/照明/除雾/广告灯/工控机等设备状态
        /// <summary>
        /// 查询制冷压缩机/风机/照明/除雾/广告灯/工控机等设备状态
        /// </summary>
        /// <param name="boxNo">机柜编号</param>
        /// <returns>设备状态集合</returns>
        public EquipmentsStatus QueryEquipmentsStatus(int boxNo)
        {
            string msg = null;
            EquipmentsStatus equipmentsStatus = new EquipmentsStatus();
            List<byte> sendData = new List<byte>() { 0x01, 0x02, 0x11, (byte)boxNo, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x0A
                && recData[2] == 0x11
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        #region 制冷设备状态
                        switch (recData[4])
                        {
                            case 0x00:
                                equipmentsStatus.制冷设备状态.IsOK = false;
                                equipmentsStatus.制冷设备状态.Msg = "制冷压缩机关闭";
                                break;
                            case 0x01:
                                equipmentsStatus.制冷设备状态.IsOK = true;
                                equipmentsStatus.制冷设备状态.Msg = "制冷压缩机打开时间，以分钟为单位";
                                break;
                            case 0xFF:
                                equipmentsStatus.制冷设备状态.IsOK = false;
                                equipmentsStatus.制冷设备状态.Msg = "不存在该控制回路";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 风机状态
                        switch (recData[5])
                        {
                            case 0x00:
                                equipmentsStatus.风机状态.IsOK = false;
                                equipmentsStatus.风机状态.Msg = "风机关闭";
                                break;
                            case 0x01:
                                equipmentsStatus.风机状态.IsOK = true;
                                equipmentsStatus.风机状态.Msg = "风机打开";
                                break;
                            case 0x02:
                                equipmentsStatus.风机状态.IsOK = false;
                                equipmentsStatus.风机状态.Msg = "不存在该控制回路";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 照明灯状态
                        switch (recData[6])
                        {
                            case 0x00:
                                equipmentsStatus.照明灯状态.IsOK = false;
                                equipmentsStatus.照明灯状态.Msg = "照明灯关闭";
                                break;
                            case 0x01:
                                equipmentsStatus.照明灯状态.IsOK = true;
                                equipmentsStatus.照明灯状态.Msg = "照明灯打开";
                                break;
                            case 0x02:
                                equipmentsStatus.照明灯状态.IsOK = false;
                                equipmentsStatus.照明灯状态.Msg = "不存在该控制回路";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 除雾器状态
                        switch (recData[7])
                        {
                            case 0x00:
                                equipmentsStatus.除雾器状态.IsOK = false;
                                equipmentsStatus.除雾器状态.Msg = "除雾器关闭";
                                break;
                            case 0x01:
                                equipmentsStatus.除雾器状态.IsOK = true;
                                equipmentsStatus.除雾器状态.Msg = "除雾器打开";
                                break;
                            case 0x02:
                                equipmentsStatus.除雾器状态.IsOK = false;
                                equipmentsStatus.除雾器状态.Msg = "不存在该控制回路";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 广告灯状态
                        switch (recData[8])
                        {
                            case 0x00:
                                equipmentsStatus.广告灯状态.IsOK = false;
                                equipmentsStatus.广告灯状态.Msg = "广告灯关闭";
                                break;
                            case 0x01:
                                equipmentsStatus.广告灯状态.IsOK = true;
                                equipmentsStatus.广告灯状态.Msg = "广告灯打开";
                                break;
                            case 0x02:
                                equipmentsStatus.广告灯状态.IsOK = false;
                                equipmentsStatus.广告灯状态.Msg = "不存在该控制回路";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 工控机/显示器/机箱风扇状态
                        switch (recData[9])
                        {
                            case 0x00:
                                equipmentsStatus.工控机显示器机箱风扇状态.IsOK = false;
                                equipmentsStatus.工控机显示器机箱风扇状态.Msg = "设备关闭";
                                break;
                            case 0x01:
                                equipmentsStatus.工控机显示器机箱风扇状态.IsOK = true;
                                equipmentsStatus.工控机显示器机箱风扇状态.Msg = "设备打开";
                                break;
                            case 0x02:
                                equipmentsStatus.工控机显示器机箱风扇状态.IsOK = false;
                                equipmentsStatus.工控机显示器机箱风扇状态.Msg = "不存在该控制回路";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 预留设备1状态
                        switch (recData[10])
                        {
                            case 0x00:
                                equipmentsStatus.预留设备1状态.IsOK = false;
                                equipmentsStatus.预留设备1状态.Msg = "预留设备1关闭";
                                break;
                            case 0x01:
                                equipmentsStatus.预留设备1状态.IsOK = true;
                                equipmentsStatus.预留设备1状态.Msg = "预留设备1打开";
                                break;
                            case 0x02:
                                equipmentsStatus.预留设备1状态.IsOK = false;
                                equipmentsStatus.预留设备1状态.Msg = "不存在该控制回路";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 预留设备2状态
                        switch (recData[11])
                        {
                            case 0x00:
                                equipmentsStatus.预留设备2状态.IsOK = false;
                                equipmentsStatus.预留设备2状态.Msg = "预留设备2关闭";
                                break;
                            case 0x01:
                                equipmentsStatus.预留设备2状态.IsOK = true;
                                equipmentsStatus.预留设备2状态.Msg = "预留设备2打开";
                                break;
                            case 0x02:
                                equipmentsStatus.预留设备2状态.IsOK = false;
                                equipmentsStatus.预留设备2状态.Msg = "不存在该控制回路";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion
                        break;
                    case 0x01:
                        throw new Exception("不存在该机柜编号");
                        break;
                    default:
                        throw new Exception("未知状态");
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                throw new Exception("货机正在执行操作，请稍后检测");
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }

            return equipmentsStatus;
        }
        #endregion

        #region 设置制冷压缩机/照明/除雾/广告灯/工控机等设备控制策略参数
        /// <summary>
        /// 设置制冷压缩机/照明/除雾/广告灯/工控机等设备控制策略参数
        /// </summary>
        /// <param name="boxNo">机柜编号</param>
        /// <param name="equipmentType">设备类型(0：制冷压缩机， 2：照明设备， 3：除雾设备， 4：广告灯， 5：工控机/显示器/机箱风扇， 6：预留设备1， 7：预留设备2)</param>
        /// <param name="temperature">目标温度(3—15℃)</param>
        /// <param name="controlMode">控制模式(0：定时开启，1：全时段开启，2：全时段关闭，如果设备类型为5（工控机/显示器/机箱风扇），控制模式不能为2（全时段关闭）)</param>
        /// <param name="periodOfTime1">定时时间段1(格式HHmmHHmm)</param>
        /// <param name="periodOfTime2">定时时间段2(格式HHmmHHmm)</param>
        /// <param name="msg">传出错误信息</param>
        /// <returns>是否设置成功</returns>
        public bool SetEquipments(int boxNo, int equipmentType, int temperature, int controlMode, string periodOfTime1, string periodOfTime2, out string msg)
        {
            //温度
            if (equipmentType == 0)
            {
                temperature = temperature >= 0 ? temperature : 128 - temperature;
            }
            else
            {
                temperature = 0xFF;
            }

            //控制模式
            if (equipmentType == 5 && controlMode == 2)
            {
                msg = "设备类型为5（工控机/显示器/机箱风扇）时，控制模式不能为2（全时段关闭）";
                return false;
            }

            //时间段
            List<byte> time1 = new List<byte>();
            List<byte> time2 = new List<byte>();
            try
            {
                time1.Add((byte)int.Parse(periodOfTime1.Substring(0, 2)));
                time1.Add((byte)int.Parse(periodOfTime1.Substring(2, 2)));
                time1.Add((byte)int.Parse(periodOfTime1.Substring(4, 2)));
                time1.Add((byte)int.Parse(periodOfTime1.Substring(6, 2)));

                time2.Add((byte)int.Parse(periodOfTime2.Substring(0, 2)));
                time2.Add((byte)int.Parse(periodOfTime2.Substring(2, 2)));
                time2.Add((byte)int.Parse(periodOfTime2.Substring(4, 2)));
                time2.Add((byte)int.Parse(periodOfTime2.Substring(6, 2)));
            }
            catch
            {
                msg = "定时时间段格式不正确";
                return false;
            }

            List<byte> sendData = new List<byte>() { 0x01, 0x0D, 0x14, 
                (byte)boxNo,
                (byte)equipmentType,
                (byte)temperature,
                (byte)controlMode};

            sendData.AddRange(time1);
            sendData.AddRange(time2);
            sendData.AddRange(new byte[] { 0x00 });
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x02
                && recData[2] == 0x14
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        msg = "设置成功";
                        return true;
                    case 0x01:
                        msg = "设置失败";
                        return false;
                    case 0x02:
                        msg = "不存在该机柜编号";
                        return false;
                    case 0x03:
                        msg = "不存在该控制回路";
                        return false;
                    case 0x04:
                        msg = "不符合设备控制策略";
                        return false;
                    default:
                        msg = "未知状态";
                        return false;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                return false;
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }
        }
        #endregion

        #region 查询制冷压缩机/照明/除雾/广告灯/工控机等设备控制策略参数
        /// <summary>
        /// 查询制冷压缩机/照明/除雾/广告灯/工控机等设备控制策略参数
        /// </summary>
        /// <param name="boxNo">机柜编号</param>
        /// <param name="equipmentType">设备类型(0：制冷压缩机， 2：照明设备， 3：除雾设备， 4：广告灯， 5：工控机/显示器/机箱风扇， 6：预留设备1， 7：预留设备2)</param>
        /// <returns>策略参数集合</returns>
        public EquipmentParameter QueryEquipment(int boxNo, int equipmentType)
        {
            string msg = null;

            EquipmentParameter equipmentParameter = new EquipmentParameter();
            List<byte> sendData = new List<byte>() { 0x01, 0x03, 0x15, (byte)boxNo, (byte)equipmentType, 0x00 };
            CommonUtil.CalCheckCode(sendData);
            byte[] recData = ReadPort(sendData.ToArray());

            if (recData.Length >= 4
                && recData[0] == 0x01
                && recData[1] == 0x0C
                && recData[2] == 0x15
                && CommonUtil.ValidCheckCode(recData))
            {
                switch (recData[3])
                {
                    case 0x00:
                        #region 目标温度值
                        equipmentParameter.目标温度值 = (recData[4] >= 128 ? -recData[4] % 128 : recData[4]).ToString() + "℃";
                        #endregion

                        #region 控制模式
                        switch (recData[5])
                        {
                            case 0x00:
                                equipmentParameter.控制模式 = "定时开启";
                                break;
                            case 0x01:
                                equipmentParameter.控制模式 = "全时段开启";
                                break;
                            case 0x02:
                                equipmentParameter.控制模式 = "全时段关闭";
                                break;
                            default:
                                throw new Exception("未知状态");
                        }
                        #endregion

                        #region 定时时间段1
                        if (recData[5] == 0) //定时开启
                        {
                            equipmentParameter.定时时间段1 = recData[6].ToString() + ":" + recData[7].ToString("00") + "-"
                                + recData[8].ToString() + ":" + recData[9].ToString("00");
                        }
                        #endregion

                        #region 定时时间段2
                        if (recData[5] == 0) //定时开启
                        {
                            equipmentParameter.定时时间段2 = recData[10].ToString() + ":" + recData[11].ToString("00") + "-"
                                + recData[12].ToString() + ":" + recData[13].ToString("00");
                        }
                        #endregion

                        equipmentParameter.IsOK = true;
                        break;
                    case 0x01:
                        equipmentParameter.IsOK = false;
                        equipmentParameter.ErrorMsg = "不存在该机柜编号";
                        break;
                    case 0x03:
                        equipmentParameter.IsOK = false;
                        equipmentParameter.ErrorMsg = "不存在控制回路";
                        break;
                    default:
                        equipmentParameter.IsOK = false;
                        equipmentParameter.ErrorMsg = "未知状态";
                        break;
                }
            }
            else if (IsRunning(recData, out msg) || !IsConnected(recData, out msg))
            {
                throw new Exception("货机正在执行操作，请稍后检测");
            }
            else
            {
                throw new Exception("货机返回的数据格式不正确");
            }

            return equipmentParameter;
        }
        #endregion

        #region 查询制冷压缩机/照明/除雾/广告灯/工控机等设备控制策略参数
        /// <summary>
        /// 查询制冷压缩机/照明/除雾/广告灯/工控机等设备控制策略参数
        /// </summary>
        /// <param name="boxNo">机柜编号</param>
        /// <returns>策略参数集合</returns>
        public EquipmentInfo QueryEquipmentAll(int boxNo)
        {
            EquipmentInfo equipmentInfo = new EquipmentInfo();

            equipmentInfo.制冷压缩机 = QueryEquipment(boxNo, 0);
            equipmentInfo.照明设备 = QueryEquipment(boxNo, 2);
            equipmentInfo.除雾设备 = QueryEquipment(boxNo, 3);
            equipmentInfo.广告灯 = QueryEquipment(boxNo, 4);
            equipmentInfo.工控机显示器机箱风扇 = QueryEquipment(boxNo, 5);
            equipmentInfo.预留设备1 = QueryEquipment(boxNo, 6);
            equipmentInfo.预留设备2 = QueryEquipment(boxNo, 7);

            return equipmentInfo;
        }
        #endregion

    }

}
