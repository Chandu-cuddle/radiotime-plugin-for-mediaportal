using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using System.Net;
using System.Xml.Serialization;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections;
using System.Timers;
using MediaPortal.GUI.Library;
using MediaPortal.Dialogs;
using MediaPortal.Util;
using MediaPortal.Localisation;
using MediaPortal.Configuration;
using MediaPortal.Player;
using MediaPortal.Playlists;
using RadioTimeOpmlApi;
using RadioTimeOpmlApi.com.radiotime.services;

namespace RadioTimePlugin
{
  [PluginIcons("RadioTimePlugin.radiotime.png", "RadioTimePlugin.radiotime_disabled.png")]
  public class RadioTimePluginGUI : BaseGui, ISetupForm
  {

    #region MapSettings class
    [Serializable]
    public class MapSettings
    {
      protected int _SortBy;
      protected int _ViewAs;
      protected bool _SortAscending;

      public MapSettings()
      {
        // Set default view
        _SortBy = 0;
        _ViewAs = (int)View.List;
        _SortAscending = true;
      }

      [XmlElement("SortBy")]
      public int SortBy
      {
        get { return _SortBy; }
        set { _SortBy = value; }
      }

      [XmlElement("ViewAs")]
      public int ViewAs
      {
        get { return _ViewAs; }
        set { _ViewAs = value; }
      }

      [XmlElement("SortAscending")]
      public bool SortAscending
      {
        get { return _SortAscending; }
        set { _SortAscending = value; }
      }
    }
    #endregion

    #region Base variables

    #endregion

    enum View
    {
      List = 0,
      Icons = 1,
      BigIcons = 2,
      Albums = 3,
      Filmstrip = 4,
    }

    #region locale vars

    private Identification iden = new Identification();
    MapSettings mapSettings = new MapSettings();
    private int oldSelection = 0;
    private StationSort.SortMethod curSorting = StationSort.SortMethod.name;

    #endregion

    #region skin connection
    [SkinControlAttribute(50)]
    protected GUIFacadeControl listControl = null;
    [SkinControlAttribute(2)]
    protected GUISortButtonControl sortButton = null;
    [SkinControlAttribute(4)]
    protected GUIButtonControl homeButton = null;
    [SkinControlAttribute(3)]
    protected GUIButtonControl btnSwitchView = null;
    [SkinControlAttribute(5)]
    protected GUIButtonControl searchButton = null;
    [SkinControlAttribute(6)]
    protected GUIButtonControl presetsButton = null;
    [SkinControlAttribute(7)]
    protected GUIButtonControl searchArtistButton = null;
    [SkinControlAttribute(8)]
    protected GUIButtonControl genresButton = null;
    [SkinControlAttribute(9)]
    protected GUIButtonControl nowPlayingButton = null;
    [SkinControlAttribute(10)]
    protected GUIButtonControl randomButton = null;
    [SkinControlAttribute(11)]
    protected GUIButtonControl localpresetsButton = null;
    [SkinControlAttribute(51)]
    protected GUIImage logoImage = null;
    #endregion

    public RadioTimePluginGUI()
    {
      GetID = GetWindowId();
      _setting.Load();
      Settings.Instance = _setting;
      iden.UserName = _setting.User;
      iden.PasswordKey = RadioTimeWebServiceHelper.HashMD5(_setting.Password);
      iden.PartnerId = "MediaPortal";
      iden.PartnerKey = "NVNxA8N$6VD1";
      grabber.Settings.User = _setting.User;
      grabber.Settings.Password = _setting.Password;
      grabber.Settings.PartnerId = _setting.PartnerId;
    }

    #region ISetupForm Members
    // return name of the plugin
    public string PluginName()
    {
      return "RadioTime";
    }
    // returns plugin description
    public string Description()
    {
      return "RadioTime";
    }
    // returns author
    public string Author()
    {
      return "Dukus";
    }
    // shows the setup dialog
    public void ShowPlugin()
    {
      SetupForm setup = new SetupForm();
      setup.ShowDialog();
    }
    // enable / disable
    public bool CanEnable()
    {
      return true;
    }
    // returns the unique id again
    public int GetWindowId()
    {
      return 25650;
    }
    // default enable?
    public bool DefaultEnabled()
    {
      return true;
    }
    // has setup gui?
    public bool HasSetup()
    {
      return true ;
    }
    // home button
    public bool GetHome(out string strButtonText, out string strButtonImage,
      out string strButtonImageFocus, out string strPictureImage)
    {
      // set the values for the buttom
      strButtonText = _setting.PluginName;

      // no image or picture
      strButtonImage = String.Empty;
      strButtonImageFocus = String.Empty;
      strPictureImage = String.Empty;

      return true;
    }
    // init the skin
    public override bool Init()
    {
      _setting.Language = GUILocalizeStrings.GetCultureName(GUILocalizeStrings.CurrentLanguage());

      updateStationLogoTimer.AutoReset = true;
      updateStationLogoTimer.Enabled = false;
      updateStationLogoTimer.Elapsed += OnDownloadTimedEvent;
      Client.DownloadFileCompleted += DownloadLogoEnd;

      Settings.NowPlaying = new RadioTimeNowPlaying();
      Settings.NowPlayingStation = new RadioTimeStation();

      ClearProps();
       
      // show the skin
      return Load(GUIGraphicsContext.Skin + @"\radiotime.xml");
    }
     //do the init before page load
    protected override void OnPageLoad()
    {
        updateStationLogoTimer.Enabled = true;
        if (!string.IsNullOrEmpty(Settings.GuideId))
        {
          grabber.GetData(string.Format("http://opml.radiotime.com/Browse.ashx?id={0}&{1}", Settings.GuideId,
                                        grabber.Settings.GetParamString()), Settings.GuideIdDescription);
          Settings.GuideId = string.Empty;
          Settings.GuideIdDescription = string.Empty;
          LoadLocalPresetStations();
        }
        else
        {
          if (grabber.Body.Count < 1)
          {
            if (_setting.ShowPresets)
            {
              grabber.GetData(_setting.StartupUrl, _setting.PluginName);
              grabber.GetData(_setting.PresetsUrl, false, Translation.Presets);
              oldSelection = 1;
            }
            else
            {
              Log.Info("RadioTime page loading :{0}", _setting.StartupUrl);
              grabber.GetData(_setting.StartupUrl, _setting.PluginName);
            }
          }
        }

      
      GUIPropertyManager.SetProperty("#header.label", " ");
      GUIPropertyManager.SetProperty("#RadioTime.Selected.NowPlaying", " ");
      GUIPropertyManager.SetProperty("#RadioTime.Selected.Subtext", " ");
      GUIPropertyManager.SetProperty("#RadioTime.Selected.Format", " ");
      GUIPropertyManager.SetProperty("#RadioTime.Selected.Reliability", "0");

      UpdateList();
      listControl.SelectedListItemIndex = oldSelection;
      
      GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_SETFOCUS, GetID, 0, 50, 0, 0, null);
      OnMessage(msg);

      if (sortButton != null)
      {
        sortButton.SortChanged += SortChanged;
      }

      // set the sort button label
      switch (curSorting)
      {
        case StationSort.SortMethod.bitrate:
          sortButton.Label = Translation.SortByBitrate;
          break;
        case StationSort.SortMethod.name:
          sortButton.Label = Translation.SortByName;
          break;
      }
      
      foreach (string name in Translation.Strings.Keys)
      {
        SetProperty("#RadioTime.Translation." + name + ".Label", Translation.Strings[name]);
      }

      g_Player.PlayBackStarted += g_Player_PlayBackStartedFromGUI;

      if (_setting.StartWithFastPreset && _setting.FirtsStart)
      {
        GUIWindowManager.ReplaceWindow(25653);
      }
      base.OnPageLoad();
    }

    void g_Player_PlayBackStartedFromGUI(g_Player.MediaType type, string filename)
    {
      if (g_Player.IsVideo)
        g_Player.ShowFullScreenWindow();
    }

    // remeber the selection on page leave
    protected override void OnPageDestroy(int new_windowId)
    {
      if (sortButton != null)
      {
        sortButton.SortChanged -= SortChanged;
      }
      g_Player.PlayBackStarted -= g_Player_PlayBackStartedFromGUI;

      oldSelection = listControl.SelectedListItemIndex;

      base.OnPageDestroy(new_windowId);
    }
    //// do the clicked action
    protected override void OnClicked(int controlId, GUIControl control, Action.ActionType actionType)
    {
      ////
      //// look for button pressed
      ////
      //// record ?
      if (actionType == Action.ActionType.ACTION_RECORD)
      {
        //ExecuteRecord();
      }
      else if (control == btnSwitchView)
      {
        switch ((View)mapSettings.ViewAs)
        {
          case View.List:
            mapSettings.ViewAs = (int)View.Icons;
            break;
          case View.Icons:
            mapSettings.ViewAs = (int)View.BigIcons;
            break;
          case View.BigIcons:
            mapSettings.ViewAs = (int)View.List;
            break;
        }
        ShowPanel();
       // GUIControl.FocusControl(GetID, listControl.GetID);
      }
      else if (control == listControl)
      {
        // execute only for enter keys
        if (actionType == Action.ActionType.ACTION_SELECT_ITEM)
        {
          // station selected
          DoListSelection();
        }
      }
      else if (control == sortButton)
      {
        //sort button selected
        OnShowSortOptions();
        GUIControl.FocusControl(GetID, listControl.GetID);
      }
      else if (control == searchArtistButton)
      {
        DoSearchArtist();
        GUIControl.FocusControl(GetID, listControl.GetID);
      }
      else if (control == searchButton)
      {
        DoSearch();
        GUIControl.FocusControl(GetID, listControl.GetID);
      }
      else if (control == homeButton)
      {
        ShowPanel();
        DoHome();
        GUIControl.FocusControl(GetID, listControl.GetID);
      }
      else if (control == genresButton)
      {
        ShowPanel();
        DoGenres();
        GUIControl.FocusControl(GetID, listControl.GetID);
      }
      else if (control == presetsButton)
      {
        ShowPanel();
        DoPresets();
        GUIControl.FocusControl(GetID, listControl.GetID);
      }
      else if (control == nowPlayingButton)
      {
        GUIWindowManager.ActivateWindow(25652);
      }
      else if (control == localpresetsButton)
      {
        GUIWindowManager.ActivateWindow(25653);
      }
      else if (control == randomButton)
      {
        GUIControl.FocusControl(GetID, listControl.GetID);
        RadioTime gr = new RadioTime();
        gr.Settings = grabber.Settings;
        gr.GetData(grabber.CurentUrl + "&filter=random");
        if (gr.Body.Count == 1)
        {
          if (!string.IsNullOrEmpty(gr.Body[0].GuidId))
            DoPlay(gr.Body[0]);
          else if (!string.IsNullOrEmpty(gr.Body[0].Text))
            ErrMessage(gr.Body[0].Text);
        }
        else
          ErrMessage(Translation.NoStationsOrShowsAvailable);
      }
      base.OnClicked(controlId, control, actionType);
    }

    private void DoHome()
    {
      Log.Debug("RadioTime page loading :{0}", _setting.StartupUrl);
      grabber.Reset();
      grabber.GetData(_setting.StartupUrl, _setting.PluginName);
      UpdateList();
    }


    private void DoGenres()
    {
      Log.Debug("RadioTime page loading :{0}", _setting.GenresUrl);
      //grabber.Reset();
      grabber.GetData(_setting.GenresUrl, Translation.Genres);
      UpdateList();
    }


    //// override action responses
    public override void OnAction(Action action)
    {
      if (action.wID == Action.ActionType.ACTION_PREVIOUS_MENU)
      {
        if (listControl.Focus)
        {
          if (grabber.Parent != null && grabber.Parent.Body.Count > 0)
          {
            DoBack();
            return;
          }
        }
      }

      if (action.wID == Action.ActionType.ACTION_PARENT_DIR)
      {
        GUIListItem item = listControl[0];

        if ((item != null) && item.IsFolder && (item.Label == ".."))
        {
          DoBack();
          return;
        }
      }
      UpdateGui();
      base.OnAction(action);
    }

    // do regulary updates
    public override void Process()
    {
      // update the gui
      //UpdateGui();
    }

    protected void OnShowSortOptions()
    {
      GUIDialogMenu dlg = (GUIDialogMenu)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
      if (dlg == null) return;
      dlg.Reset();
      dlg.SetHeading(Translation.Sorting); // Sort options

      dlg.Add(Translation.SortByName); // name
      dlg.Add(Translation.SortByBitrate); // bitrate
      dlg.Add(Translation.NoSorting); // bitrate
      // set the focus to currently used sort method
      dlg.SelectedLabel = (int)curSorting;

      // show dialog and wait for result
      dlg.DoModal(GetID);
      if (dlg.SelectedId == -1) return;

      if(dlg.SelectedLabelText==Translation.SortByBitrate)
      {
          curSorting = StationSort.SortMethod.bitrate;
          sortButton.Label = Translation.SortByBitrate;
      }
      else if (dlg.SelectedLabelText == Translation.SortByName)
      {
        curSorting = StationSort.SortMethod.name;
        sortButton.Label = Translation.SortByName;
      }
      else
      {
        curSorting = StationSort.SortMethod.none;
        sortButton.Label = Translation.NoSorting;
      }

      sortButton.IsAscending = mapSettings.SortAscending;
      UpdateList();
    }
 
    #endregion
    #region helper func's

    private void DoPresets()
    {
      grabber.GetData(_setting.PresetsUrl, false, Translation.Presets);
      UpdateList();
    }

    private void DoListSelection()
    {
      GUIWaitCursor.Show();
      GUIListItem selectedItem = listControl.SelectedListItem;
      if (selectedItem != null)
      {
        if (selectedItem.Label != "..")
        {
          RadioTimeOutline radioItem = ((RadioTimeOutline)selectedItem.MusicTag);
          switch (radioItem.Type)
          {
            case RadioTimeOutline.OutlineType.link:
              if (string.IsNullOrEmpty(radioItem.Url) && !string.IsNullOrEmpty(radioItem.GuidId))
              {
                grabber.GetData(string.Format("http://opml.radiotime.com/Browse.ashx?id={0}&{1}", radioItem.GuidId,
                                              grabber.Settings.GetParamString()), selectedItem.Label);
              }
              else if (!string.IsNullOrEmpty(radioItem.Url))
              {
                grabber.GetData(radioItem.Url, selectedItem.Label);                
              }
              UpdateList();
              break;
            case RadioTimeOutline.OutlineType.audio:
              DoPlay(radioItem);
              break;
            default:
              if (string.IsNullOrEmpty(radioItem.Url) && !string.IsNullOrEmpty(radioItem.GuidId))
              {
                grabber.GetData(string.Format("http://opml.radiotime.com/Browse.ashx?id={0}&{1}", radioItem.GuidId,
                                              grabber.Settings.GetParamString()), selectedItem.Label);
                UpdateList();
              }
              else if (!string.IsNullOrEmpty(radioItem.Url))
              {
                grabber.GetData(radioItem.Url, selectedItem.Label);
                UpdateList();
              }
              break;
          }
        }
        else
        {
          grabber.Prev();
          UpdateList();
        }
      }
      GUIWaitCursor.Hide();
      //throw new Exception("The method or operation is not implemented.");
    }



    private void DoSearchArtist()
    {
      string searchString = "";

      GUIDialogMenu dlg = (GUIDialogMenu)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
      if (dlg == null) return;
      dlg.Reset();
      dlg.SetHeading(Translation.SearchHistory);
      dlg.Add(string.Format("<{0}>", Translation.NewSearch));
      for (int i = _setting.ArtistSearchHistory.Count; i > 0; i--)
      {
        dlg.Add(_setting.ArtistSearchHistory[i - 1]);
      }
      dlg.DoModal(GetID);
      if (dlg.SelectedId == -1)
        return;
      searchString = dlg.SelectedLabelText;
      if (searchString == string.Format("<{0}>", Translation.NewSearch))
        searchString = "";

      // display an virtual keyboard
      var keyboard = (VirtualKeyboard)GUIWindowManager.GetWindow((int)Window.WINDOW_VIRTUAL_KEYBOARD);
      if (null == keyboard) return;
      keyboard.Reset();
      keyboard.Text = searchString;
      keyboard.DoModal(GetWindowId());
      if (keyboard.IsConfirmed)
      {
        // input confirmed -- execute the search
        searchString = keyboard.Text;
      }

      if ("" != searchString)
      {
        grabber.SearchArtist(searchString, Translation.SearchArtist);
        UpdateList();
        if (_setting.ArtistSearchHistory.Contains(searchString.Trim()))
          _setting.ArtistSearchHistory.Remove(searchString.Trim());
        _setting.ArtistSearchHistory.Add(searchString.Trim());
        _setting.Save();
      }
    }

    private void DoSearch()
    {
      string searchString = "";

      GUIDialogMenu dlg = (GUIDialogMenu)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
      if (dlg == null) return;
      dlg.Reset();
      dlg.SetHeading(Translation.SearchHistory);
      dlg.Add(string.Format("<{0}>", Translation.NewSearch));
      for (int i = _setting.SearchHistory.Count; i > 0; i--)
      {
        dlg.Add(_setting.SearchHistory[i - 1]);
      }
      dlg.DoModal(GetID);
      if (dlg.SelectedId == -1)
        return;
      
      searchString = dlg.SelectedLabelText;
      if (searchString == string.Format("<{0}>", Translation.NewSearch))
        searchString = "";

      // display an virtual keyboard
      VirtualKeyboard keyboard = (VirtualKeyboard)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_VIRTUAL_KEYBOARD);
      if (null == keyboard) return;
      keyboard.Reset();
      keyboard.Text = searchString;
      keyboard.DoModal(GetWindowId());
      if (keyboard.IsConfirmed)
      {
        // input confirmed -- execute the search
        searchString = keyboard.Text;
      }

      if ("" != searchString)
      {
        grabber.Search(searchString, Translation.Search);
        UpdateList();
        if (_setting.SearchHistory.Contains(searchString.Trim()))
          _setting.SearchHistory.Remove(searchString.Trim());
        _setting.SearchHistory.Add(searchString.Trim());
        _setting.Save();
      }
    }
    
    private void DoBack()
    {
      if (grabber.Parent != null)
      {
        grabber.Prev();
        UpdateList();
      }
      else
      {
        GUIWindowManager.ShowPreviousWindow();
      }
    }
    
    public void UpdateList()
    {
      updateStationLogoTimer.Enabled = false;
      downloaQueue.Clear();
      GUIControl.ClearControl(GetID, listControl.GetID);
      if (grabber.Parent != null && grabber.Parent.Body.Count > 0)
      {
        GUIListItem item = new GUIListItem();
        // and add station name & bitrate
        item.Label = "..";
        item.Label2 = "(" + grabber.Parent.Body.Count.ToString() + ")";
        item.OnItemSelected += item_OnItemSelected;
        item.IsFolder = true;
        item.IconImage = "defaultFolderBack.png";
        item.IconImageBig = "DefaultFolderBackBig.png";
        item.MusicTag = null;
        listControl.Add(item);
      }
      foreach (RadioTimeOutline body in grabber.Body)
      {
        GUIListItem item = new GUIListItem();
        // and add station name & bitrate
        item.Label = body.Text;
        item.Label2 = body.Bitrate;
        item.ThumbnailImage = GetStationLogoFileName(body);
        item.IconImage = GetStationLogoFileName(body);
        item.IsFolder = false;
        item.OnItemSelected += item_OnItemSelected;
        item.MusicTag = body;
        listControl.Add(item);
        DownloadStationLogo(body);
        switch (body.Type)
        {
          case RadioTimeOutline.OutlineType.audio:
            if (string.IsNullOrEmpty(item.IconImage))
            {
              item.IconImage = "defaultMyRadio.png";
              item.IconImageBig = "defaultMyRadioBig.png";
            }
            item.IsFolder = false;
            break;
          case RadioTimeOutline.OutlineType.link:
            if (string.IsNullOrEmpty(item.IconImage))
            {
              item.IconImage = "defaultFolder.png";
              item.IconImageBig = "defaultFolderBig.png";
            }
            item.IsFolder = true;
            break;
          case RadioTimeOutline.OutlineType.unknow:
            {
              item.IconImage = "defaultFolder.png";
              item.IconImageBig = "defaultFolderBig.png";
            }
            item.IsFolder = true;
            break;
          default:
            break;
        }
      }
      updateStationLogoTimer.Enabled = true;
      if (curSorting != StationSort.SortMethod.none)
        listControl.Sort(new StationSort(curSorting, mapSettings.SortAscending));

      GUIPropertyManager.SetProperty("#itemcount", grabber.Body.Count + " " + Translation.Objects);
      //GUIPropertyManager.SetProperty("#header.label", grabber.Head.Title);
      GUIPropertyManager.SetProperty("#header.label", grabber.NavigationTitle);

      if (grabber.CurentUrl.Contains("id="))
        randomButton.Disabled = false;
      else
        randomButton.Disabled = true;
      
      ShowPanel();
    }

    void item_OnItemSelected(GUIListItem item, GUIControl parent)
    {
      listControl.FilmstripView.InfoImageFileName = item.ThumbnailImage;
      UpdateGui();
    }

    void ShowPanel()
    {
      int itemIndex = listControl.SelectedListItemIndex;
      if (mapSettings.ViewAs == (int)View.BigIcons)
      {
        listControl.View = GUIFacadeControl.ViewMode.LargeIcons;
      }
      else if (mapSettings.ViewAs == (int)View.Albums)
      {
        listControl.View = GUIFacadeControl.ViewMode.AlbumView;
      }
      else if (mapSettings.ViewAs == (int)View.Icons)
      {
        listControl.View = GUIFacadeControl.ViewMode.SmallIcons;
      }
      else if (mapSettings.ViewAs == (int)View.List)
      {
        listControl.View = GUIFacadeControl.ViewMode.List;
      }
      else if (mapSettings.ViewAs == (int)View.Filmstrip)
      {
        listControl.View = GUIFacadeControl.ViewMode.Filmstrip;
      }
      if (itemIndex > -1)
      {
        GUIControl.SelectItemControl(GetID, listControl.GetID, itemIndex);
      }
     
    }

    protected override void OnShowContextMenu()
    {
      GUIListItem selectedItem = listControl.SelectedListItem;
      if (selectedItem != null)
      {
        if (selectedItem.Label != ".." && selectedItem.MusicTag != null && !selectedItem.IsFolder)
        {
          try
          {
            var dlg = (GUIDialogMenu)GUIWindowManager.GetWindow((int)Window.WINDOW_DIALOG_MENU);
            if (dlg == null)
              return;
            dlg.Reset();
            dlg.SetHeading(498); // menu
            RadioTimeStation station = new RadioTimeStation { Grabber = grabber };

            station.Get(((RadioTimeOutline)selectedItem.MusicTag).GuidId);

            bool show = false;
            if (station.IsPreset)
            {
              dlg.Add(Translation.RemoveFromFavorites);
              show = true;
            }
            else
            {
              dlg.Add(Translation.AddToFavorites);
              show = true;
            }

            if (station.HasSchedule)
            {
              dlg.Add(Translation.ShowGiuide);
              show = true;
            }

            if (!show)
              return;

            dlg.DoModal(GetID);
            if (dlg.SelectedId == -1)
              return;
            if (dlg.SelectedLabelText == Translation.AddToFavorites)
              AddToFavorites(((RadioTimeOutline)selectedItem.MusicTag).GuidId);
            if (dlg.SelectedLabelText == Translation.RemoveFromFavorites)
              RemoveFavorites(((RadioTimeOutline)selectedItem.MusicTag).PresetId);

            if (dlg.SelectedLabelText == Translation.ShowGiuide)
            {
              MiniGuide miniGuide = (MiniGuide)GUIWindowManager.GetWindow(25651);
              miniGuide.GuidId = ((RadioTimeOutline)selectedItem.MusicTag).GuidId;
              miniGuide.grabber = new RadioTime();
              miniGuide.grabber.Settings = grabber.Settings;
              miniGuide.DoModal(GetID);
            }
          }
          catch (System.Web.Services.Protocols.SoapException ex)
          {
            Log.Error("[RadioTime] Comunication error or wrong user name or password ");
            Log.Error(ex);
          }
        }
      }
    }

    /// <summary>
    /// Adds to favorites.
    /// </summary>
    /// <param name="p">The station id.</param>
    private void AddToFavorites(string presetid)
    {

      List<RadioTimeOutline> tempresets = new List<RadioTimeOutline>();
      
      string folderid = "";
      RadioTime tempGrabber = new RadioTime();
      tempGrabber.Settings = _setting;
      tempGrabber.GetData(_setting.PresetsUrl, false, Translation.Presets);

      var dlg = (GUIDialogMenu)GUIWindowManager.GetWindow((int)Window.WINDOW_DIALOG_MENU);
      if (dlg == null)
        return;
      dlg.Reset();
      dlg.SetHeading(Translation.SelectPresetFolder);

      foreach (RadioTimeOutline body in tempGrabber.Body)
      {
        dlg.Add(body.Text);
      }
      
      dlg.DoModal(GetID);
      if (dlg.SelectedId == -1)
        return;
      folderid = tempGrabber.Body[dlg.SelectedId - 1].GuidId;

      tempGrabber.GetData(tempGrabber.Body[dlg.SelectedId - 1].Url);

      for (int i = 0; i < Settings.LOCAL_PRESETS_NUMBER; i++)
      {
        tempresets.Add(new RadioTimeOutline());
      }

      foreach (RadioTimeOutline body in tempGrabber.Body)
      {
        if (!string.IsNullOrEmpty(body.PresetNumber) && body.PresetNumberAsInt < Settings.LOCAL_PRESETS_NUMBER)
        {
          tempresets[body.PresetNumberAsInt] = body;
        }
      }

      dlg.Reset();
      dlg.SetHeading(Translation.SelectPresetNumber);

      for (int i = 1; i < Settings.LOCAL_PRESETS_NUMBER; i++)
      {
        RadioTimeOutline outline = tempresets[i];
        if (string.IsNullOrEmpty(outline.Text))
          dlg.Add(string.Format("<{0}>", Translation.Empty));
        else
          dlg.Add(outline.Text);
      }


      dlg.DoModal(GetID);
      if (dlg.SelectedId == -1)
        return;

      try
      {
        grabber.AddPreset(presetid, folderid, dlg.SelectedId.ToString());
        //UpdateList();
      }
      catch (Exception)
      {
        ErrMessage(Translation.ComunicationError);
      }
    }

    private void AddToLocalPreset(string presetid)
    {
      var dlg = (GUIDialogMenu) GUIWindowManager.GetWindow((int) Window.WINDOW_DIALOG_MENU);
      if (dlg == null)
        return;
      dlg.Reset();

      for (int i = 0; i < Settings.LOCAL_PRESETS_NUMBER; i++)
      {
        if (_setting.PresetStations.Count > i + 1)
        {
          if (string.IsNullOrEmpty(_setting.PresetStations[i].Text))
            dlg.Add(string.Format("<{0}>", Translation.Empty));
          else
          {
            GUIListItem item = new GUIListItem(_setting.PresetStations[i].Text);
            item.IconImage = GetStationLogoFileName(_setting.PresetStations[i]);
            item.IconImageBig = item.IconImage;
            item.PinImage = item.IconImage;
            dlg.Add(item);
          }
        }
      }

      dlg.DoModal(GetID);
      if (dlg.SelectedId == -1)
        return;
      _setting.Save();
    }

    /// <summary>
    /// Removes the favorites.
    /// </summary>
    /// <param name="p">The Station id.</param>
    private void RemoveFavorites(string presetid)
    {
      try
      {
        grabber.RemovePreset(presetid);
        UpdateList();
      }
      catch (Exception)
      {
        ErrMessage(Translation.ComunicationError);
      }
    }


    public void UpdateGui()
    {
      GUIListItem selectedItem = listControl.SelectedListItem;
      if (selectedItem != null)
      {
        if (selectedItem.MusicTag !=null)
        {
          RadioTimeOutline radioItem = ((RadioTimeOutline)selectedItem.MusicTag);
          SetProperty("#selectedthumb", " ");
          logoImage.SetFileName("");
          Process();
          SetProperty("#selectedthumb", selectedItem.IconImageBig);
          logoImage.SetFileName(DownloadStationLogo(radioItem));

          UpdateSelectedLabels(radioItem);
        }
        else
        {
          logoImage.SetFileName(string.Empty);
          GUIPropertyManager.SetProperty("#RadioTime.Selected.NowPlaying", " ");
          GUIPropertyManager.SetProperty("#RadioTime.Selected.Subtext", " ");
          GUIPropertyManager.SetProperty("#RadioTime.Selected.Format", " ");
          GUIPropertyManager.SetProperty("#RadioTime.Selected.Reliability", "0");
        }
      }

      string textLine = string.Empty;
      View view = (View)mapSettings.ViewAs;
      bool sortAsc = mapSettings.SortAscending;
      switch (view)
      {
        case View.List:
          textLine = GUILocalizeStrings.Get(101);
          break;
        case View.Icons:
          textLine = GUILocalizeStrings.Get(100);
          break;
        case View.BigIcons:
          textLine = GUILocalizeStrings.Get(417);
          break;
        case View.Albums:
          textLine = GUILocalizeStrings.Get(529);
          break;
        case View.Filmstrip:
          textLine = GUILocalizeStrings.Get(733);
          break;
      }
      
      GUIControl.SetControlLabel(GetID, btnSwitchView.GetID, textLine);

    }


    void SortChanged(object sender, SortEventArgs e)
    {
      // save the new state
      mapSettings.SortAscending = e.Order != SortOrder.Descending;
      // update the list
      UpdateList();
      //UpdateButtonStates();
      GUIControl.FocusControl(GetID, ((GUIControl)sender).GetID);
    }

    #endregion

    #region download manager





    private void DownloadLogoEnd(object sender, AsyncCompletedEventArgs e)
    {
      if (e.Error == null)
      {
        File.Copy(Path.GetTempPath() + @"\station.png", curentDownlodingFile.FileName, true);
        UpdateGui();
      }
    }            

    #endregion
  }
}
