﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using ExcelDna.Integration;

namespace NNX.AddIn
{
    public class Resizer
    {
        private static readonly Queue<ExcelReference> ResizeJobs = new Queue<ExcelReference>();

        // This function will run in the UDF context.
        // Needs extra protection to allow multithreaded use.
        public static object Resize(object[,] array)
        {
            var caller = XlCall.Excel(XlCall.xlfCaller) as ExcelReference;
            if (caller == null)
                return array;

            var rows = array.GetLength(0);
            var columns = array.GetLength(1);

            if ((caller.RowLast - caller.RowFirst + 1 != rows) ||
                (caller.ColumnLast - caller.ColumnFirst + 1 != columns))
            {
                // Size problem: enqueue job, call async update and return #N/A
                // TODO: Add guard for ever-changing result?
                EnqueueResize(caller, rows, columns);
                AsyncRunMacro("DoResizing");
                return ExcelError.ExcelErrorNA;
            }

            // Size is already OK - just return result
            return array;
        }

        static void EnqueueResize(ExcelReference caller, int rows, int columns)
        {
            var target = new ExcelReference(caller.RowFirst, caller.RowFirst + rows - 1, caller.ColumnFirst, caller.ColumnFirst + columns - 1, caller.SheetId);
            ResizeJobs.Enqueue(target);
        }

        public static void DoResizing()
        {
            while (ResizeJobs.Count > 0)
            {
                DoResize(ResizeJobs.Dequeue());
            }
        }

        static void DoResize(ExcelReference target)
        {
            try
            {
                // Get the current state for reset later

                XlCall.Excel(XlCall.xlcEcho, false);

                // Get the formula in the first cell of the target
                var formula = (string)XlCall.Excel(XlCall.xlfGetCell, 41, target);
                var firstCell = new ExcelReference(target.RowFirst, target.RowFirst, target.ColumnFirst, target.ColumnFirst, target.SheetId);

                var isFormulaArray = (bool)XlCall.Excel(XlCall.xlfGetCell, 49, target);
                if (isFormulaArray)
                {
                    var oldSelectionOnActiveSheet = XlCall.Excel(XlCall.xlfSelection);
                    XlCall.Excel(XlCall.xlfActiveCell);

                    // Remember old selection and select the first cell of the target
                    var firstCellSheet = (string)XlCall.Excel(XlCall.xlSheetNm, firstCell);
                    XlCall.Excel(XlCall.xlcWorkbookSelect, firstCellSheet);
                    var oldSelectionOnArraySheet = XlCall.Excel(XlCall.xlfSelection);
                    XlCall.Excel(XlCall.xlcFormulaGoto, firstCell);

                    // Extend the selection to the whole array and clear
                    XlCall.Excel(XlCall.xlcSelectSpecial, 6);
                    var oldArray = (ExcelReference)XlCall.Excel(XlCall.xlfSelection);

                    oldArray.SetValue(ExcelEmpty.Value);
                    XlCall.Excel(XlCall.xlcSelect, oldSelectionOnArraySheet);
                    XlCall.Excel(XlCall.xlcFormulaGoto, oldSelectionOnActiveSheet);
                }
                // Get the formula and convert to R1C1 mode
                var isR1C1Mode = (bool)XlCall.Excel(XlCall.xlfGetWorkspace, 4);
                var formulaR1C1 = formula;
                if (!isR1C1Mode)
                {
                    // Set the formula into the whole target
                    formulaR1C1 = (string)XlCall.Excel(XlCall.xlfFormulaConvert, formula, true, false, ExcelMissing.Value, firstCell);
                }
                // Must be R1C1-style references
                object ignoredResult;
                var retval = XlCall.TryExcel(XlCall.xlcFormulaArray, out ignoredResult, formulaR1C1, target);
                if (retval != XlCall.XlReturn.XlReturnSuccess)
                {
                    // TODO: Consider what to do now!?
                    // Might have failed due to array in the way.
                    firstCell.SetValue("'" + formula);
                }
            }
            finally
            {
                XlCall.Excel(XlCall.xlcEcho, true);
            }
        }

        // Most of this from the newsgroup: http://groups.google.com/group/exceldna/browse_thread/thread/a72c9b9f49523fc9/4577cd6840c7f195
        private static readonly TimeSpan BackoffTime = TimeSpan.FromSeconds(1);
        static void AsyncRunMacro(string macroName)
        {
            // Do this on a new thread....
            var newThread = new Thread(delegate ()
            {
                while (true)
                {
                    try
                    {
                        RunMacro(macroName);
                        break;
                    }
                    catch (COMException cex)
                    {
                        if (IsRetry(cex))
                        {
                            Thread.Sleep(BackoffTime);
                            continue;
                        }
                        // TODO: Handle unexpected error
                        return;
                    }
                    catch (Exception)
                    {
                        // TODO: Handle unexpected error
                        return;
                    }
                }
            });
            newThread.Start();
        }

        static void RunMacro(string macroName)
        {
            object xlApp = "";
            try
            {
                xlApp = ExcelDnaUtil.Application;
                xlApp.GetType().InvokeMember("Run", BindingFlags.InvokeMethod, null, xlApp, new object[] { macroName });
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException;
            }
            finally
            {
                if (xlApp is string && ((string)xlApp) != "")
                    Marshal.ReleaseComObject(xlApp);
            }
        }

        const uint RPC_E_SERVERCALL_RETRYLATER = 0x8001010A;
        const uint VBA_E_IGNORE = 0x800AC472;

        static bool IsRetry(COMException e)
        {
            var errorCode = (uint)e.ErrorCode;
            switch (errorCode)
            {
                case RPC_E_SERVERCALL_RETRYLATER:
                case VBA_E_IGNORE:
                    return true;
                default:
                    return false;
            }
        }
    }
}
