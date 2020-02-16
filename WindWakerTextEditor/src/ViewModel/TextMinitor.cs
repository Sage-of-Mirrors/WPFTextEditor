﻿using GameFormatReader.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using WArchiveTools;
using WArchiveTools.FileSystem;
using WindEditor.Minitors.Text;
using WindEditor.ViewModel;
using Microsoft.Win32;

namespace WindEditor.Minitors
{
    public class TextMinitor : IMinitor, INotifyPropertyChanged
    {
        #region IMinitor Interface
        public MenuItem GetMenuItem()
        {
            return new MenuItem()
            {
                Header = "Text Editor",
                ToolTip = "Editor for the game's main text bank.",
                Command = OpenMinitorCommand,
            };
        }

        public void InitModule(WDetailsViewViewModel details_view_model)
        {
            //details_view_model.TypeCustomizations.Add(typeof(MessageReference).Name, new MessageReferenceTypeCustomization(this));
        }

        public bool RequestCloseModule()
        {
            if (!m_IsDataDirty)
                return true;

            MessageBoxResult result = MessageBox.Show("You have unsaved changes to the text data. Save them?", "Unsaved Text Changes", MessageBoxButton.YesNoCancel);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    OnRequestSaveMessageData();
                    return true;
                case MessageBoxResult.No:
                    return true;
                case MessageBoxResult.Cancel:
                    return false;
                default:
                    return true;
            }
        }
        #endregion

        #region INotifyPropertyChanged Interface
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private string m_OpenFilePath;

        public static Dictionary<char, char> CharacterRemap;

        public ICommand OpenMinitorCommand { get { return new RelayCommand(x => OnRequestOpenFile()); } }
        public ICommand OpenFileCommand { get { return new RelayCommand(x => OnRequestOpenFile()); } }
        public ICommand ExportCSVCommand { get { return new RelayCommand(x => OnRequestExportCSV(), x => Messages != null); } }
        public ICommand SaveMessageDataCommand { get { return new RelayCommand(x => OnRequestSaveMessageData(), x => Messages != null); } }
        public ICommand SaveMessageDataAsCommand { get { return new RelayCommand(x => OnRequestSaveMessageDataAs(), x => Messages != null); } }
        public ICommand AddMessageCommand { get { return new RelayCommand(x => OnRequestAddMessage(), x => Messages != null); } }

        public TextMinitor()
        {
            CharacterRemap = new Dictionary<char, char>();
            CharacterRemap.Add((char)0x84, 'Ğ');
            CharacterRemap.Add((char)0x85, 'Ž');
            CharacterRemap.Add((char)0x86, 'Š');
            CharacterRemap.Add((char)0x87, 'Ą');
            CharacterRemap.Add((char)0x88, 'Į');
            CharacterRemap.Add((char)0x89, 'ğ');
            CharacterRemap.Add((char)0x8A, 'ž');
            CharacterRemap.Add((char)0x8B, 'š');
            CharacterRemap.Add((char)0x8C, 'ą');
            CharacterRemap.Add((char)0x8D, 'į');
            CharacterRemap.Add((char)0x8E, 'Ų');
            CharacterRemap.Add((char)0x8F, 'ų');
        }

        public List<Message> Messages
        {
            get { return m_Messages; }
            set
            {
                if (value != m_Messages)
                {
                    m_Messages = value;
                    OnPropertyChanged("Messages");
                }
            }
        }

        public Message SelectedMessage
        {
            get { return m_SelectedMessage; }
            set
            {
                if (value != m_SelectedMessage)
                {
                    if (m_SelectedMessage != null)
                    {
                        m_SelectedMessage.PropertyChanged -= OnSelectedMessagePropertyChanged;
                    }

                    m_SelectedMessage = value;
                    OnPropertyChanged("SelectedMessage");

                    if (DetailsModel != null)
                    {
                        DetailsModel.ReflectObject(m_SelectedMessage);
                    }

                    if (m_SelectedMessage != null)
                    {
                        m_SelectedMessage.PropertyChanged += OnSelectedMessagePropertyChanged;
                    }
                }
            }
        }

        public string SearchFilter
        {
            get { return m_SearchFilter; }
            set
            {
                if (value != m_SearchFilter)
                {
                    m_SearchFilter = value;
                    OnPropertyChanged("SearchFilter");

                    CollectionViewSource.GetDefaultView(TextListView.ItemsSource).Refresh();
                }
            }
        }

        public string WindowTitle
        {
            get { return m_IsDataDirty ? m_WindowTitle + "*" : m_WindowTitle; }
            set
            {
                if (value != m_WindowTitle)
                {
                    m_WindowTitle = value;
                    OnPropertyChanged("WindowTitle");
                }
            }
        }

        public TextEncoding OriginalEncoding
        {
            get
            {
                return m_OriginalEncoding;
            }
            set
            {
                if (value != m_OriginalEncoding)
                {
                    m_OriginalEncoding = value;
                    OnPropertyChanged("OriginalEncoding");
                }
            }
        }

        public WDetailsViewViewModel DetailsModel;
        public ListView TextListView;

        private List<Message> m_Messages;
        private Message m_SelectedMessage;
        private VirtualFilesystemDirectory m_MessageArchive;

        private string m_SearchFilter;
        private string m_WindowTitle;

        private TextEncoding m_OriginalEncoding;

        private bool m_IsDataDirty;

        private void OnRequestSaveMessageData()
        {
            SaveMessageArchive(m_OpenFilePath);
        }

        private void OnRequestSaveMessageDataAs()
        {

        }

        private void OnRequestAddMessage()
        {
            ushort highest_msg_id = GetHighestID();

            // There are many empty message entries in the vanilla file.
            // We will first try to find a message with an ID of 0.
            Message new_message = Messages.Find(x => x.MessageID == 0);

            // If we find a message with a MessageID of 0, we will give it a valid
            // ID and focus it for the user.
            if (new_message != null)
            {
                new_message.MessageID = (ushort)(highest_msg_id + 1);
                new_message.LineCount = 1;
                new_message.ItemImage = ItemID.No_item;
            }
            // If the user has used up all the blank messages, we have no
            // choice but to add a completely new message to the file.
            else
            {
                new_message = new Message();
                new_message.MessageID = (ushort)(highest_msg_id + 1);
                new_message.Index = Messages.Count;

                Messages.Add(new_message);
                OnPropertyChanged("Messages");
            }

            // This allows us to update the UI to show the new MessageID even if the new message
            // is on-screen when ScrollIntoView() is called.
            ICollectionView view = CollectionViewSource.GetDefaultView(TextListView.ItemsSource);
            view.Refresh();

            TextListView.SelectedItem = new_message;
            TextListView.ScrollIntoView(new_message);
        }

        private ushort GetHighestID()
        {
            ushort highest_id = 0;

            foreach (Message mes in Messages)
            {
                if (highest_id < mes.MessageID)
                    highest_id = mes.MessageID;
            }

            return highest_id;
        }

        public bool FilterMessages(object item)
        {
            if (string.IsNullOrEmpty(SearchFilter))
            {
                return true;
            }
            else
            {
                Message mes = item as Message;

                if (SearchFilter.Contains(':'))
                {
                    string[] split_filter = SearchFilter.Split(':');

                    if (split_filter.Length < 2 || !int.TryParse(split_filter[1], out int val))
                    {
                        return true;
                    }

                    if (split_filter[0].ToLowerInvariant() == "msgid")
                    {
                        return mes.MessageID == val;
                    }
                    else if (split_filter[0].ToLowerInvariant() == "index")
                    {
                        return Messages.IndexOf(mes) == val;
                    }
                    else
                    {
                        return true;
                    }
                }
                else
                {
                    return mes.Text.IndexOf(SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
        }

        private bool TryLoadMessageArchive()
        {
            string bmgres_path = m_OpenFilePath;

            if (!File.Exists(bmgres_path))
            {
                return false;
            }

            m_MessageArchive = ArchiveUtilities.LoadArchive(bmgres_path);

            VirtualFilesystemFile text_bank = m_MessageArchive.GetFileAtPath("zel_00.bmg");

            using (MemoryStream strm = new MemoryStream(text_bank.Data))
            {
                EndianBinaryReader reader = new EndianBinaryReader(strm, Endian.Big);
                LoadMessageData(reader);
            }

            return true;
        }

        private void LoadMessageData(EndianBinaryReader reader)
        {
            List<Message> new_message_list = new List<Message>();

            string file_magic = reader.ReadString(8);
            int file_size = reader.ReadInt32();
            int section_count = reader.ReadInt32();

            m_OriginalEncoding = (TextEncoding)reader.ReadByte();

            reader.Skip(15);

            int inf1_offset = (int)reader.BaseStream.Position;

            string inf1_magic = reader.ReadString(4);
            int inf1_size = reader.ReadInt32();
            ushort message_count = reader.ReadUInt16();
            short message_size = reader.ReadInt16();

            reader.Skip(4);

            int text_bank_start = inf1_offset + inf1_size + 8;

            Encoding enc = Encoding.ASCII;
            switch (m_OriginalEncoding)
            {
                case TextEncoding.CP1252:
                    enc = Encoding.GetEncoding(1252);
                    break;
                case TextEncoding.Shift_JIS:
                    enc = Encoding.GetEncoding(932);
                    break;
                case TextEncoding.UTF_16:
                    enc = Encoding.BigEndianUnicode;
                    break;
                case TextEncoding.UTF_8:
                    enc = Encoding.UTF8;
                    break;
            }

            for (int i = 0; i < message_count; i++)
            {
                Message msg = new Message(reader, text_bank_start, enc);
                msg.Index = i;

                new_message_list.Add(msg);
            }

            Messages = new_message_list;
        }

        private void SaveMessageArchive(string file_path)
        {
            SaveMessageData();

            ArchiveUtilities.WriteArchive(file_path, m_MessageArchive);
        }

        private void SaveMessageData()
        {
            VirtualFilesystemFile text_bank = m_MessageArchive.GetFileAtPath("zel_00.bmg");

            Encoding enc = Encoding.ASCII;
            switch (m_OriginalEncoding)
            {
                case TextEncoding.CP1252:
                    enc = Encoding.GetEncoding(1252);
                    break;
                case TextEncoding.Shift_JIS:
                    enc = Encoding.GetEncoding(932);
                    break;
                case TextEncoding.UTF_16:
                    enc = Encoding.BigEndianUnicode;
                    break;
                case TextEncoding.UTF_8:
                    enc = Encoding.UTF8;
                    break;
            }

            using (MemoryStream new_bmg_strm = new MemoryStream())
            {
                EndianBinaryWriter bmg_writer = new EndianBinaryWriter(new_bmg_strm, Endian.Big);
                bmg_writer.Write("MESGbmg1".ToCharArray());
                bmg_writer.Write(0);
                bmg_writer.Write(2);
                bmg_writer.Write((byte)m_OriginalEncoding);
                bmg_writer.Write(new byte[15]);

                using (MemoryStream text_data_strm = new MemoryStream())
                {
                    EndianBinaryWriter text_data_writer = new EndianBinaryWriter(text_data_strm, Endian.Big);
                    text_data_writer.Write((byte)0);

                    using (MemoryStream message_data_strm = new MemoryStream())
                    {
                        EndianBinaryWriter message_data_writer = new EndianBinaryWriter(message_data_strm, Endian.Big);

                        foreach (Message m in m_Messages)
                        {
                            m.Save(message_data_writer, text_data_writer, enc);
                        }

                        int delta = 16;//WMath.Pad16Delta(message_data_strm.Length);

                        bmg_writer.Write("INF1".ToCharArray());
                        bmg_writer.Write((uint)(message_data_strm.Length + 16 + delta));
                        bmg_writer.Write((ushort)m_Messages.Count);
                        bmg_writer.Write((ushort)0x18);
                        bmg_writer.Write(0);

                        bmg_writer.Write(message_data_strm.ToArray());

                        for (int i = 0; i < delta; i++)
                            bmg_writer.Write((byte)0);
                    }

                    bmg_writer.Write("DAT1".ToCharArray());
                    bmg_writer.Write((uint)text_data_strm.Length + 8);

                    bmg_writer.Write(text_data_strm.ToArray());
                }

                text_bank.Data = new_bmg_strm.ToArray();
            }

            m_IsDataDirty = false;
            OnPropertyChanged("WindowTitle");
        }

        private void OnSelectedMessagePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            m_IsDataDirty = true;
            OnPropertyChanged("WindowTitle");
        }

        public void OnUserRequestOpenReference(ushort id)
        {
            /*OnRequestOpenTextEditor();

            Message requested_message = Messages.Find(x => x.MessageID == id);

            if (requested_message != null)
            {
                m_MinitorWindow.TextListView.SelectedItem = requested_message;
                m_MinitorWindow.TextListView.ScrollIntoView(requested_message);
            }
            else
            {

            }*/
        }

        public void OnRequestOpenFile()
        {
            OpenFileDialog m_openFile = new OpenFileDialog();

            m_openFile.FileName = "bmgres";
            m_openFile.DefaultExt = ".arc";
            m_openFile.Filter = "arc files (*.arc)|*.arc";

            if (m_openFile.ShowDialog() == true)
            {
                m_OpenFilePath = m_openFile.FileName;
                if (TryLoadMessageArchive())
                {
                    SelectedMessage = Messages[0];
                    WindowTitle = "Text Editor - " + m_OpenFilePath;

                    CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(TextListView.ItemsSource);
                    view.Filter = FilterMessages;

                    SearchFilter = "";
                }
            }
        }

        public void OnRequestExportCSV()
        {
            SaveFileDialog save = new SaveFileDialog();

            save.DefaultExt = ".csv";
            save.Filter = "Comma-Separated Values (*.csv)|*.csv";

            if (save.ShowDialog() == true)
            {
                File.WriteAllText(save.FileName, BuildCSV());
            }
        }

        private string BuildCSV()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("ID,Text");

            foreach (Message m in Messages)
            {
                builder.AppendLine($"{ m.Index },\"{ m.Text.Replace("\"", "\\\"") }\"");
            }

            return builder.ToString();
        }
    }
}
