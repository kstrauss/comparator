﻿//
//  THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
//  KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//  IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR
//  PURPOSE.
//
//  License: GNU Lesser General Public License (LGPLv3)
//
//  Email: pumpet.net@gmail.com
//  Git: https://github.com/Pumpet/comparator
//  Copyright (C) Alex Rozanov, 2016 
//
// Special thanks to Pavel Torgashov for his excellent FastColoredTextBox component!
// https://github.com/PavelTorgashov/FastColoredTextBox
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using Excel = Microsoft.Office.Interop.Excel;
using Common;

namespace SqlSource
{
  public partial class SqlView : Form, ISqlView  
  {
    BindingSource bs = new BindingSource();
    BindingSource bsFields = new BindingSource();
    Func<BindingSource, Array> bsFieldsList = (b) => { return b.OfType<string>().Select(x => new { Value = x }).ToArray(); }; // get from List<string> to grid
    SynchronizationContext sync { get; set; }
    Dictionary<string, string> propNames;
    bool inProc; // true if query is executed
    string currSQL = "";
    const int maxClipCells = 100000; // max grid cells to clipboard
    const int maxExcelRows = 100000; // max grid rows for excel
    //~~~~~~~~ ISqlView Members ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    #region
    //-------------------------------------------------------------------------
    public event Func<Action<string,Exception>, bool> TestConnect;
    public event Action<string, TaskContext> GetData;
    public event Action StopGetData;
    //-------------------------------------------------------------------------
    /* Set data object property names for use in binding */
    public void SetDataProps(Dictionary<string, string> names)
    {
      propNames = names ?? new Dictionary<string, string>();
    }
    //-------------------------------------------------------------------------
    /* Set data objects in BindingSources */
    public void SetData(object inData, object providers, object fields)
    {
      if (inData != null) bs.DataSource = inData;
      if (providers != null) cbProvider.DataSource = providers;
      if (fields != null) bsFields.DataSource = fields;
    }
    #endregion
    //~~~~~~~~ Init ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    #region
    //-------------------------------------------------------------------------
    public SqlView()
    {
      InitializeComponent();
      SetTabs(splitH, splitH.Panel2, splitH.Panel1, splitV, pTop, txtSQL,
        pTabs, tabs, tabResult, tabFields, dgResult, dgFields,
        pSource, tpSource, cbProvider, tbServer, tbDB, tbLogin, tbPwd, tbConnStr, bTestConn,
        pCommandTimeout, numCommandTimeout);
    }
    //-------------------------------------------------------------------------
    /* Set tab orders in order of parameters */
    void SetTabs(params Control[] x) 
    {
      Action<Control> CancelTabs = null;
      CancelTabs = (c) =>
      {
        foreach (Control item in c.Controls)
        {
          item.TabStop = false; // (item is Panel || item is ContainerControl);
          item.TabIndex = 0;
          CancelTabs(item);
        }
      };

      CancelTabs(this); 

      for (int i = 0; i < x.Length; i++)
      {
        x[i].TabStop = true;
        x[i].TabIndex = i;
      }
    }
    //-------------------------------------------------------------------------
    private void FormSource_Load(object sender, EventArgs e)
    {
      lblStatus.Text = "";
      sync = SynchronizationContext.Current;
      SetDoubleBuffered(dgResult, true);
      SetDoubleBuffered(dgFields, true);
      dgFields.EnableHeadersVisualStyles = false;
      SetBindings();
    }
    //-------------------------------------------------------------------------
    public static void SetDoubleBuffered(Control control, bool setting) // prevent blinking
    {
      System.Reflection.BindingFlags bFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
      control.GetType().GetProperty("DoubleBuffered", bFlags).SetValue(control, setting, null);
    }
    //-------------------------------------------------------------------------
    void SetBindings()
    {
      cbProvider.DataBindings.Clear();
      cbProvider.DataBindings.Add("SelectedItem", bs, propNames["Provider"]);
      tbServer.DataBindings.Clear();
      tbServer.DataBindings.Add("Text", bs, propNames["Server"]);
      tbDB.DataBindings.Clear();
      tbDB.DataBindings.Add("Text", bs, propNames["DB"]);
      tbLogin.DataBindings.Clear();
      tbLogin.DataBindings.Add("Text", bs, propNames["Login"]);
      tbPwd.DataBindings.Clear();
      tbPwd.DataBindings.Add("Text", bs, propNames["Pwd"]);
      tbConnStr.DataBindings.Clear();
      tbConnStr.DataBindings.Add("Text", bs, propNames["ConnStr"]);
      txtSQL.DataBindings.Clear();
      txtSQL.DataBindings.Add("Text", bs, propNames["SQL"]);
      numCommandTimeout.DataBindings.Clear();
      numCommandTimeout.DataBindings.Add("Value", bs, propNames["CommandTimeout"]);
      dgFields.AutoGenerateColumns = false;
      dgFields.DataSource = bsFieldsList(bsFields);
      dgFields.Columns["ColName"].DataPropertyName = "Value";
    }
    #endregion
    //~~~~~~~~ Handlers ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    #region
    //-------------------------------------------------------------------------
    private void FormSource_FormClosing(object sender, FormClosingEventArgs e)
    {
      //inProc = false;
      if (StopGetData != null)
      {
        lblStatus.Text = "Cancelling query...";
        StopGetData();
      }
      e.Cancel = false;
      EndEdit();
      lblStatus.Text = "";
    }
    //-------------------------------------------------------------------------
    private void FormSource_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.KeyCode == Keys.F5 && e.Modifiers == Keys.None)
        bData.PerformClick();
      if (e.KeyCode == Keys.F5 && e.Modifiers == Keys.Shift)
        bStop.PerformClick();
      if (e.KeyCode == Keys.Escape)
        Close();
    }
    //-------------------------------------------------------------------------
    private void dgResult_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Control && e.KeyCode == Keys.C && dgResult.SelectedCells.Count > maxClipCells) 
      {
        MessageBox.Show(string.Format("Sorry, we offer only {0} cells in Clipboard", maxClipCells), "", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        DataGridViewSelectedCellCollection sc = dgResult.SelectedCells;
        dgResult.ClearSelection();
        for (int i = sc.Count - 1; i >= sc.Count - maxClipCells; i--)
        {
          sc[i].Selected = true;
        }
      }
    }
    //-------------------------------------------------------------------------
    private void txtSQL_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Control && e.KeyCode == Keys.Tab)
      {
        if (e.Shift)
          tbServer.Focus();
        else
          tabs.Focus();
      }
    }
    #endregion
    //~~~~~~~~ Connect Properties ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    #region 
    //-------------------------------------------------------------------------
    private void bSource_Click(object sender, EventArgs e)
    {
      splitH.Panel1Collapsed = !splitH.Panel1Collapsed;
      bSource.Image = !splitH.Panel1Collapsed ? Properties.Resources.Collapse : Properties.Resources.Expand;
    }
    //-------------------------------------------------------------------------
    private void cbProvider_SelectedIndexChanged(object sender, EventArgs e)
    {
      bool b = ((ProviderType)(sender as ComboBox).SelectedItem == ProviderType.OleDB || (ProviderType)(sender as ComboBox).SelectedItem == ProviderType.ODBC);
      tbServer.Enabled = !b;
      tbDB.Enabled = !b;
      tbLogin.Enabled = !b;
      tbPwd.Enabled = !b;
      tbConnStr.Enabled = b;
    }
    #endregion
    //~~~~~~~~ Commands ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    #region
    //-------------------------------------------------------------------------
    private void bTestConn_Click(object sender, EventArgs e)
    {
      EndEdit();
      if (TestConnect != null)
      {
        Cursor = Cursors.WaitCursor;
        try
        {
          bool ok = TestConnect(ErrorHandler);
          Cursor = Cursors.Default;
          if (ok) ShowMess("Connection successful!");
        }
        finally
        {
          Cursor = Cursors.Default;
        }
      }
    }
    //-------------------------------------------------------------------------
    private void bData_Click(object sender, EventArgs e)
    {
      currSQL = txtSQL.SelectedText;
      InProc(true);
      EndEdit();
      tabs.SelectTab(tabResult);
      dgResult.DataSource = null;
      if (GetData != null) // start getting data process in controller
      {
        lblStatus.Text = "Executing query...";
        GetData(currSQL,
          new TaskContext() { ViewContext = sync, OnProgress = TaskProgress, OnFinish = TaskFinish, OnError = ErrorHandler });
      }
    }
    //-------------------------------------------------------------------------
    private void bStop_Click(object sender, EventArgs e)
    {
      if (StopGetData != null)
      {
        lblStatus.Text = "Cancelling query...";
        StopGetData(); // stop getting data process in controller
      }
    }
    //-------------------------------------------------------------------------
    private void bExcel_Click(object sender, EventArgs e)
    {
      try
      {
        ExcelProc.GridToExcel(tabs.SelectedTab == tabResult ? dgResult : dgFields, maxExcelRows);
      }
      catch (Exception ex)
      {
        ShowMess(ex.ToString(), true);
      }
    }
    #endregion
    //~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    #region
    //-------------------------------------------------------------------------
    void EndEdit()
    {
      bs.EndEdit();
      txtSQL.DataBindings[0].WriteValue();
      numCommandTimeout.DataBindings[0].WriteValue();
    }
    //-------------------------------------------------------------------------
    void InProc(bool b)
    {
      inProc = b;
      bData.Enabled = !b;
      bStop.Enabled = b;
    }
    //-------------------------------------------------------------------------
    void ErrorHandler(string mess, Exception ex)
    {
      ShowMess(mess, true, ex);
    }
    //-------------------------------------------------------------------------
    void ShowMess(string mess, bool isErr = false, object objError = null)
    {
      MessageBoxIcon icon = MessageBoxIcon.None;
      if (isErr)
      {
        icon = MessageBoxIcon.Error;
        if (objError != null)
          mess = string.Format("{0}\n\n{1}", mess, (objError is Exception) ? ((Exception)objError).Message : objError.ToString());
      }
      MessageBox.Show(mess, "", MessageBoxButtons.OK, icon);
    }
    //-------------------------------------------------------------------------
    void TaskProgress(int step, string msg)
    {
      Func<int> Step = () => // calculate step, gradually slowing...
      {
        int d = 1000;
        int min = d * 100;
        double k = pBar.Maximum * 4;
        if (step == 0) step = 1;
        if (step > min) k = k - (1 - pBar.Value / (double)pBar.Maximum) * (step - min) / d;
        if (k < pBar.Maximum) k = pBar.Maximum;
        step = (int)((-k / Math.Pow(step, 1.0 / 4.0) + pBar.Maximum));
        if (step < pBar.Value) step = pBar.Value;
        if (step > pBar.Maximum) step = pBar.Maximum;
        if (step < pBar.Minimum) step = pBar.Minimum + 1;
        return step;
      };

      if (inProc)
      {
        pBar.Value = Step();
        lblStatus.Text = msg;
      }
    }
    //-------------------------------------------------------------------------
    void TaskFinish(object result, string msg)
    {
      if (inProc)
      {
        pBar.Value = 0;
        dgResult.DataSource = result;
        lblStatus.Text = msg;
        InProc(false);
      }
      dgFields.DataSource = bsFieldsList(bsFields);
    }
    #endregion
  }
}

