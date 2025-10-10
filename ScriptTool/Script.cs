using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ScriptTool
{
    // Jump reference tracking structure
    public struct JumpReference
    {
        public int Address { get; set; }
        public int TargetAddress { get; set; }
        public string InstructionType { get; set; }

        public JumpReference(int address, int targetAddress, string instructionType)
        {
            Address = address;
            TargetAddress = targetAddress;
            InstructionType = instructionType;
        }
    }
	
	public class StringMetadata
	{
		public long Address { get; set; }
		public string Text { get; set; }
		public int Type { get; set; }
		public string NamePrefix { get; set; } // Store the injected name separately
		
		public StringMetadata(long address, string text, int type, string namePrefix = "")
		{
			Address = address;
			Text = text;
			Type = type;
			NamePrefix = namePrefix;
		}
	}
	
    public class Script
    {
        // Fields
        private string _disassembly = string.Empty;
        private readonly List<StringMetadata> _collectedStrings = new();
        private readonly List<JumpReference> _jumpReferences = new();
        private readonly Dictionary<byte, string> _textTokenTable = new();

        private Encoding _encoding = Encoding.GetEncoding(932);
        
        private BinaryReader _reader = null!;
        private StringBuilder _sb = null!;
        private int _currentAddress;
        
        private readonly Dictionary<byte, (string Name, Action<byte> Handler)> _opcodeMap;
        
        public Encoding Encoding
        {
            get => _encoding;
            set => _encoding = value ?? Encoding.GetEncoding(932);
        }

        // Properties
        public string Disassembly => _disassembly;
        public IReadOnlyList<StringMetadata> CollectedStrings => _collectedStrings;
        public IReadOnlyList<JumpReference> JumpReferences => _jumpReferences;
        public IReadOnlyDictionary<byte, string> TextTokenTable => _textTokenTable;
        
        public Script()
        {
            _opcodeMap = new Dictionary<byte, (string, Action<byte>)>
            {
                { 0x00, ("EXIT", HandleExit) },
                { 0x01, ("SET_VIEWPORT_POSITION", HandleSetViewPortPosition) },
                { 0x02, ("SET_VIEWPORT_SIZE", HandleSetViewPortSize) },
                { 0x03, ("LOAD_ANM", HandleLoadAnm) },
                { 0x04, ("UNLOAD_ANM", HandleUnloadAnm) },
                { 0x05, ("FADE_IN_VIEWPORT_GRAYSCALE", HandleFadeInViewportGrayscale) },
                { 0x06, ("FADE_OUT_VIEWPORT_GRAYSCALE", HandleFadeOutViewportGrayscale) },
                { 0x07, ("LOAD_PALETTE_FROM_ANM", HandleLoadPaletteFromAnm) },
                { 0x08, ("ENABLE_DRAWING_STATE", HandleEnableUnknownDrawingState) },
                { 0x09, ("DISABLE_DRAWING_STATE", HandleDisableUnknownDrawingState) },
                { 0x0A, ("SET_BUFFER_PROPERTIES", HandleSetBufferProperties) },
                { 0x0B, ("RESET_BUFFER_PROPERTY", HandleResetBufferProperty) },
                { 0x0C, ("UPDATE_BUFFER_PROPERTY_0C", HandleUpdateBufferProperty0C) },
                { 0x0D, ("RESET_EACH_ANM_PROPERTY", HandleResetEachAnmProperty) },
                { 0x0E, ("WAIT_FOR_INPUT", HandleWaitForInput) },
                { 0x0F, ("WAIT_FOR_INPUT_ALT", HandleWaitForInput) },
                { 0x10, ("CLEAR_SCROLL_PROPERTIES", HandleClearScrollProperties) },
                { 0x12, ("SET_BASIC_SCROLL_PROPERTIES", HandleSetBasicScrollProperties) },
                { 0x13, ("SLEEP_MILLISECONDS", HandleSleepMilliseconds) },
                { 0x14, ("JUMP_TO", HandleJumpTo) },
                { 0x15, ("ON_INPUT_JUMP_TO", HandleOnInputJumpTo) },
                { 0x16, ("RIGHTCLICK_JUMP_TO", HandleRightClickJumpTo) },
                { 0x17, ("SCROLL_VIEWPORT", HandleScrollViewport) },
                { 0x18, ("WAIT_FOR_SCROLL", HandleWaitForScroll) },
                { 0x19, ("PLAY_MIDI", HandlePlayMidi) },
                { 0x1A, ("STOP_MIDI", HandleStopMidi) },
                { 0x1B, ("LOAD_MIDI", HandleLoadMidi) },
                { 0x1D, ("PLAY_PCM", HandlePlayPcm) },
                { 0x1E, ("STOP_PCM", HandleStopPcm) },
                { 0x1F, ("STOP_PCM_ON_NEXT_REFRESH", HandleStopPcmNextRefresh) },
                { 0x20, ("SET_TEXT_AREA", HandleSetTextArea) },
                { 0x21, ("DISPLAY_TEXT", HandleDisplayText) },
                { 0x22, ("SET_TEXT_INDENT", HandleSetTextIndent) },
                { 0x23, ("SET_FONT_SIZE", HandleSetFontSize) },
                { 0x24, ("SET_TEXT_TOKEN", HandleSetTextToken) },
                { 0x25, ("GO_SUB_JUMP", HandleGoSubJump) },
                { 0x26, ("RETURN", HandleReturn) },
                { 0x27, ("OPCODE_27", HandleOpcode27) },
                { 0x28, ("MODIFY_PALETTE_WITH_EFFECT", HandleModifyPaletteWithEffect) },
                { 0x29, ("EXIT_JUMP_TO", HandleExitJumpTo) },
                { 0x2A, ("DISABLE_EXIT_MENU", HandleDisableExitMenu) },
                { 0x2B, ("LOAD_JUMP_TO", HandleLoadJumpTo) },
                { 0x2C, ("DISABLE_LOAD_MENU", HandleDisableLoadMenu) },
                { 0x2D, ("CLEAR_ON_JUMP_ADDRESSES", HandleClearOnJumpAddresses) },
                { 0x2E, ("JUMP_IF_REGISTER_EQUAL", HandleJumpIfRegisterEqual) },
                { 0x2F, ("JUMP_IF_REGISTER_NOT_EQUAL", HandleJumpIfRegisterNotEqual) },
                { 0x30, ("JUMP_IF_REGISTER_LESS_THAN_OR_EQUAL", HandleJumpIfRegisterLessOrEqual) },
                { 0x31, ("JUMP_IF_REGISTER_GREATER_THAN_OR_EQUAL", HandleJumpIfRegisterGreaterOrEqual) },
                { 0x32, ("READ_REGISTER", HandleReadRegister) },
                { 0x33, ("JUMP_IF_LAST_READ_NOT_EQUAL", HandleJumpIfLastReadNotEqual) },
                { 0x34, ("WRITE_TO_MEM", HandleWriteToMem) },
                { 0x35, ("INC_REGISTER", HandleIncRegister) },
                { 0x36, ("DEC_REGISTER", HandleDecRegister) },
                { 0x37, ("ADD", HandleAdd) },
                { 0x38, ("OR", HandleOr) },
                { 0x39, ("AND", HandleAnd) },
                { 0x3A, ("FADE_TO_BLACK", HandleFadeToBlack) },
                { 0x3B, ("LOAD_STATE", HandleLoadState) },
                { 0x3C, ("SAVE_STATE", HandleSaveState) },
                { 0x3D, ("PAINT_BLACK_RECTANGLE", HandlePaintBlackRectangle) },
                { 0x3E, ("DISPLAY_CHOICE_TEXT", HandleDisplayChoiceText) },
                { 0x3F, ("SET_HOT_ZONE_SEPARATOR", HandleSetHotZoneSeparator) },
                { 0x40, ("SET_SAVE_MENU_ENABLED", HandleSetSaveMenuEnabled) },
                { 0x41, ("MODIFY_PALETTE", HandleModifyPalette) },
                { 0x42, ("WAIT_FOR_INPUT_OR_PCM", HandleWaitForInputOrPcm) },
                { 0x43, ("WORD_OPERATION", HandleWordOperation) },
                { 0x44, ("OPCODE_44", HandleOpcode44) },
                { 0x45, ("WRITE_DPAD_DIRECTION", HandleWriteDpadDirection) },
                { 0x46, ("EVALUATE_DPAD_DIRECTION", HandleEvaluateDpadDirection) },
                { 0x47, ("OPCODE_47", HandleOpcode47) },
                { 0x48, ("JUMP_IF_LAST_READ_AND_VALUE_EQUALS_ZERO", HandleJumpIfLastReadAndZero) },
                { 0x49, ("WRITE_BUFFER_PROPERTY_VALUE", HandleWriteBufferPropertyValue) },
                { 0x4A, ("LOAD_WIN", HandleLoadWin) },
                { 0x4B, ("SET_CARET_POSITION", HandleSetCaretPosition) },
                { 0x4C, ("VIDEO_EFFECT_4C", HandleVideoEffect4C) },
                { 0x4D, ("DISABLE_BUFFER_PROPERTIES_SYNC", HandleDisableBufferSync) },
                { 0x4E, ("ENABLE_BUFFER_PROPERTIES_SYNC", HandleEnableBufferSync) },
                { 0x4F, ("UPDATE_BUFFER_PROPERTY_WITH_SYNC", HandleUpdateBufferPropertySync) },
                { 0x50, ("FADE_IN_VIEWPORT_RGB", HandleFadeInViewportRgb) },
                { 0x51, ("FADE_OUT_VIEWPORT_RGB", HandleFadeOutViewportRgb) },
                { 0x52, ("SET_MASK_STATE", HandleSetUnknownMaskState) },
                { 0x53, ("REPEAT_JUMP_TO", HandleRepeatJumpTo) },
                { 0x54, ("DISABLE_REPEAT_MENU", HandleDisableRepeatMenu) },
                { 0x55, ("SET_VISIBLE_PALETTE_RANGE", HandleSetVisiblePaletteRange) },
                { 0x56, ("LOAD_EXTENDED_STATE", HandleLoadExtendedState) },
                { 0x57, ("SAVE_EXTENDED_STATE", HandleSaveExtendedState) },
                { 0x58, ("COPY_REG_TO_FROM", HandleCopyRegToFrom) },
                { 0x59, ("DIV_OP", HandleDivOp) },
                { 0x5A, ("UPDATE_BUFFER_PROPERTY_5A", HandleUpdateBufferProperty5A) },
                { 0x5B, ("BUFFER_PROPERTIES_DOUBLE_WORD_ARRAY_OPERATION", HandleBufferDoubleWordArrayOp) },
                { 0x5C, ("RESET_BUFFER_SCROLL_PROPERTIES", HandleResetBufferScrollProperties) },
                { 0x5D, ("SET_MIDI_PROPERTIES_5D", HandleSetUnknownMidiProperties5D) },
                { 0x5E, ("FADE_OUT_MIDI", HandleFadeOutMidi) },
                { 0x5F, ("NO_OP", HandleNoOp) },
                { 0x60, ("FADE_MIDI_TO_VOLUME", HandleFadeMidiToVolume) },
                { 0x61, ("SET_EXTENDED_SCROLL_PROPERTIES", HandleSetExtendedScrollProperties) },
                { 0x62, ("SET_TEXT_BOX_STATE", HandleSetTextBoxState) },
                { 0x63, ("SET_PCM_FLAG", HandleSetPcmFlag) },
                { 0x64, ("APPLY_PALETTE_LOOKUP_TABLE", HandleApplyPaletteLookupTable) },
                { 0x65, ("MAX_CD_AUDIO_STREAMS", HandleMaxCdAudioStreams) },
                { 0x66, ("OPCODE_66", HandleOpcode66) },
                { 0x67, ("ENABLE_TEXT_HISTORY", HandleEnableTextHistory) },
                { 0x68, ("ADD_TEXT_HISTORY_ENTRY", HandleAddTextHistoryEntry) },
                { 0x69, ("DISPLAY_CHOICE_TEXT_WITH_ADDRESSES", HandleDisplayChoiceTextWithAddresses) },
                { 0x6A, ("WRITE_RANDOM", HandleWriteRandom) },
                { 0x6B, ("LOAD_PCM_WITH_DELAY", HandleLoadPcmWithDelay) },
                { 0x6C, ("REPEAT_PCM_INFINITE", HandleRepeatPcmInfinite) },
                { 0x6D, ("JUMP_IF_BUFFER_PROPERTY_NOT_ZERO", HandleJumpIfBufferPropertyNotZero) },
                { 0x6E, ("SET_STATE_6E", HandleSetUnknownState6E) },
                { 0x6F, ("JUMP_IF_REGISTER_EQUAL_REGISTER", HandleJumpIfRegisterEqualRegister) },
                { 0x70, ("JUMP_IF_REGISTER_NOT_EQUAL_REGISTER", HandleJumpIfRegisterNotEqualRegister) },
                { 0x71, ("JUMP_IF_REGISTER_LESS_THAN_OR_EQUAL_REGISTER", HandleJumpIfRegisterLessOrEqualRegister) },
                { 0x72, ("JUMP_IF_REGISTER_GREATER_THAN_OR_EQUAL_REGISTER", HandleJumpIfReg1GEReg2) },
                { 0x73, ("DRAW_TEXT_ON_BUFFER", HandleDrawTextOnBuffer) },
                { 0x74, ("SET_TEXT_COLOR", HandleSetTextColor) },
                { 0x75, ("SET_TEXT_POSITION", HandleSetTextPosition) },
                { 0x76, ("SHOW_DIALOG", HandleShowDialog) },
                { 0x78, ("SET_BYTE_78", HandleSetUnknownByte78) },
                { 0x79, ("SET_CHARACTER_FACING_FROM_REGISTER", HandleSetCharacterFacingFromRegister) },
                { 0x7A, ("JUMP_IF_NOT_ZERO", HandleJumpIfNotZero) },
                { 0x7B, ("LOAD_BMP_IN_MEMORY", HandleLoadBmp) },
            };
        }
        
        public void Load(string filePath) => Load(filePath, _encoding);
        
        public void Load(string filePath, Encoding encoding)
        {
            _encoding = encoding ?? Encoding.GetEncoding(932);
            using var reader = new BinaryReader(File.OpenRead(filePath));
            _collectedStrings.Clear();
            _jumpReferences.Clear();
            Parse(reader);
        }
        
        private void AddJumpReference(int address, uint target, string type)
        {
            _jumpReferences.Add(new JumpReference(address, (int)target, type));
        }
		
		private void AddStringMetaData(long address, string text, int type, string prefixName = "")
		{
			_collectedStrings.Add(new StringMetadata(address, text, type, prefixName));
		}
        
        private void Parse(BinaryReader reader)
        {
            _reader = reader;
            _sb = new StringBuilder();
            reader.BaseStream.Position = 0;

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                _currentAddress = (int)reader.BaseStream.Position;
                byte opcode = reader.ReadByte();

                if (_opcodeMap.TryGetValue(opcode, out var info))
                {
                    try { info.Handler(opcode); }
                    catch (Exception ex)
                    {
                        _sb.AppendLine($"{_currentAddress:X8} | {info.Name} [ERROR: {ex.Message}]");
                    }
                }
                else
                {
                    _sb.AppendLine($"{_currentAddress:X8} | UNKNOWN_OPCODE 0x{opcode:X2}");
                }
            }

            _disassembly = _sb.ToString();
            _sb.Clear();
            _reader?.Dispose();
        }
        
        // Handler implementations
        private void HandleExit(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | EXIT");
        }
        
        private void HandleSetViewPortPosition(byte opcode) 
        {
            ushort posX = _reader.ReadUInt16();
            ushort posY = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | SET_VIEWPORT_POSITION PosX={posX}, PosY={posY}");
        }
        
        private void HandleSetViewPortSize(byte opcode) 
        {
            ushort width = _reader.ReadUInt16();
            ushort height = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | SET_VIEWPORT_SIZE Width={width}, Height={height}");
        }
        
        private void HandleLoadAnm(byte opcode) 
        {
            byte id = _reader.ReadByte();
            string filename = _reader.ReadNullTerminatedString(_encoding);
            _sb.AppendLine($"{_currentAddress:X8} | LOAD_ANM ID={id}, File=\"{filename}\"");
        }
        
        private void HandleUnloadAnm(byte opcode) 
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | UNLOAD_ANM ID={id}");
        }
        
        private void HandleFadeInViewportGrayscale(byte opcode) 
        {
            byte param1 = _reader.ReadByte();
            ushort param2 = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | FADE_IN_VIEWPORT_GRAYSCALE Param1={param1}, Param2={param2}");
        }
        
        private void HandleFadeOutViewportGrayscale(byte opcode) 
        {
            byte param1 = _reader.ReadByte();
            ushort param2 = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | FADE_OUT_VIEWPORT_GRAYSCALE Param1={param1}, Param2={param2}");
        }
        
        private void HandleLoadPaletteFromAnm(byte opcode) 
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | LOAD_PALETTE_FROM_ANM ID={id}");
        }
        
        private void HandleEnableUnknownDrawingState(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | ENABLE_DRAWING_STATE");
        }
        
        private void HandleDisableUnknownDrawingState(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | DISABLE_DRAWING_STATE");
        }
        
        private void HandleSetBufferProperties(byte opcode) 
        {
            byte b1 = _reader.ReadByte();
            ushort w1 = _reader.ReadUInt16();
            ushort w2 = _reader.ReadUInt16();
            byte b2 = _reader.ReadByte();
            byte b3 = _reader.ReadByte();
            byte b4 = _reader.ReadByte();
            byte b5 = _reader.ReadByte();
            var pairs = _reader.ReadPairs();
            _sb.AppendLine($"{_currentAddress:X8} | SET_BUFFER_PROPERTIES B={b1}, W1={w1}, W2={w2}, B2={b2}, B3={b3}, B4={b4}, B5={b5}, Pairs=[{string.Join(",", pairs)}]");
        }
        
        private void HandleResetBufferProperty(byte opcode) 
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | RESET_BUFFER_PROPERTY ID={id}");
        }
        
        private void HandleUpdateBufferProperty0C(byte opcode) 
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | UPDATE_BUFFER_PROPERTY_0C ID={id}");
        }
        
        private void HandleResetEachAnmProperty(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | RESET_EACH_ANM_PROPERTY");
        }
        
        private void HandleWaitForInput(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | WAIT_FOR_INPUT");
        }
        
        private void HandleClearScrollProperties(byte opcode) 
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | CLEAR_SCROLL_PROPERTIES ID={id}");
        }
        
        private void HandleSetBasicScrollProperties(byte opcode) 
        {
            byte b1 = _reader.ReadByte();
            byte b2 = _reader.ReadByte();
            byte b3 = _reader.ReadByte();
            ushort w1 = _reader.ReadUInt16();
            ushort w2 = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | SET_BASIC_SCROLL_PROPERTIES B1={b1}, B2={b2}, B3={b3}, W1={w1}, W2={w2}");
        }
        
        private void HandleSleepMilliseconds(byte opcode) 
        {
            ushort milliseconds = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | SLEEP_MILLISECONDS Time={milliseconds}ms");
        }
        
        private void HandleJumpTo(byte opcode) 
        {
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_TO");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_TO Target=0x{target:X4}");
        }
        
        private void HandleOnInputJumpTo(byte opcode) 
        {
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "ON_INPUT_JUMP_TO");
            _sb.AppendLine($"{_currentAddress:X8} | ON_INPUT_JUMP_TO Target=0x{target:X4}");
        }
        
        private void HandleRightClickJumpTo(byte opcode) 
        {
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "RIGHTCLICK_JUMP_TO");
            _sb.AppendLine($"{_currentAddress:X8} | RIGHTCLICK_JUMP_TO Target=0x{target:X4}");
        }
        
        private void HandleScrollViewport(byte opcode) 
        {
            byte b = _reader.ReadByte();
            ushort w1 = _reader.ReadUInt16();
            ushort w2 = _reader.ReadUInt16();
            ushort w3 = _reader.ReadUInt16();
            ushort w4 = _reader.ReadUInt16();
            ushort w5 = _reader.ReadUInt16();
            ushort w6 = _reader.ReadUInt16();
            ushort w7 = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | SCROLL_VIEWPORT B={b}, W1={w1}, W2={w2}, W3={w3}, W4={w4}, W5={w5}, W6={w6}, W7={w7}");
        }
        
        private void HandleWaitForScroll(byte opcode) 
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | WAIT_FOR_SCROLL ID={id}");
        }
        
        private void HandlePlayMidi(byte opcode) 
        {
            byte b1 = _reader.ReadByte();
            byte b2 = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | PLAY_MIDI B1={b1}, B2={b2}");
        }
        
        private void HandleStopMidi(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | STOP_MIDI");
        }
        
        private void HandleLoadMidi(byte opcode) 
        {
            byte id = _reader.ReadByte();
            string filename = _reader.ReadNullTerminatedString(_encoding);
            _sb.AppendLine($"{_currentAddress:X8} | LOAD_MIDI ID={id}, File=\"{filename}\"");
        }
        
        private void HandlePlayPcm(byte opcode) 
        {
            byte id = _reader.ReadByte();
            string filename = _reader.ReadNullTerminatedString(_encoding);
            _sb.AppendLine($"{_currentAddress:X8} | PLAY_PCM ID={id}, File=\"{filename}\"");
        }
        
        private void HandleStopPcm(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | STOP_PCM");
        }
        
        private void HandleStopPcmNextRefresh(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | STOP_PCM_ON_NEXT_REFRESH");
        }
        
        private void HandleSetTextArea(byte opcode) 
        {
            ushort w1 = _reader.ReadUInt16();
            ushort w2 = _reader.ReadUInt16();
            byte b1 = _reader.ReadByte();
            byte b2 = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_TEXT_AREA W1={w1}, W2={w2}, B1={b1}, B2={b2}");
        }
        
		private void HandleDisplayText(byte opcode)
		{
			string prefixNote = string.Empty;
			byte b1 = _reader.PeekByte();
			
			if (b1 == 0x01)
			{
				_reader.ReadByte();
				byte nameIdx = _reader.ReadByte();
				if (_textTokenTable.TryGetValue(nameIdx, out var nameText))
					prefixNote = nameText;
					// prefixNote = "";
			}
			
			long stringOffset = _reader.BaseStream.Position; // capture actual string start
			StringBuilder textBuilder = new StringBuilder();
			
			while (true)
			{
				byte b = _reader.ReadByte();
				
				if (b == 0x00)
					break;
					
				if (b > 0x80)
				{
					byte b2 = _reader.ReadByte();
					ushort word = (ushort)((b << 8) | b2);
					
					switch (word)
					{
						case 0x814F: // Clear ('＾')
							textBuilder.Append(_encoding.GetString(new byte[] { b, b2 }));
							break;
						case 0x818F: // Newline ('￥')
							textBuilder.Append("\\n");
							break;
						case 0x8190: // Global string token
							byte tokenId = _reader.ReadByte();
							// if (_textTokenTable.TryGetValue(tokenId, out var tokenText))
								// textBuilder.Append(tokenText);
							break;
						default: // Double-byte character ( Normal sjis text processing)
							textBuilder.Append(_encoding.GetString(new byte[] { b, b2 }));
							break;
					}
				}
				else
				{
					textBuilder.Append((char)b);
				}
			}
			
			string text = textBuilder.ToString();
			string resolvedText = string.IsNullOrEmpty(prefixNote)
				? text
				: prefixNote + (string.IsNullOrEmpty(text) ? "" : " " + text);
				
			AddStringMetaData(stringOffset, text, 0, prefixNote); // use actual string offset
			_sb.AppendLine($"{_currentAddress:X8} | DISPLAY_TEXT Text=\"{text}\"");
		}

        
        private void HandleSetTextIndent(byte opcode) 
        {
            byte indent = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_TEXT_INDENT Indent={indent}");
        }
        
        private void HandleSetFontSize(byte opcode) 
        {
            byte size = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_FONT_SIZE Size={size}");
        }
        
		private void HandleSetTextToken(byte opcode)
		{
			byte id = _reader.ReadByte();

			long stringOffset = _reader.BaseStream.Position; // remember string start
			string text = _reader.ReadNullTerminatedString(_encoding);
			
			id = (byte)(id+1);
			_textTokenTable[id] = text;
			AddStringMetaData(stringOffset, text, 1); // use stringOffset now

			_sb.AppendLine($"{_currentAddress:X8} | SET_TEXT_TOKEN ID={id}, Text=\"{text}\"");
		}

        private void HandleGoSubJump(byte opcode) 
        {
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "GO_SUB_JUMP");
            _sb.AppendLine($"{_currentAddress:X8} | GO_SUB_JUMP Target=0x{target:X4}");
        }
        
        private void HandleReturn(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | RETURN");
        }
		
		private void HandleOpcode27(byte opcode)
        {
            byte b = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | OPCODE_27 Param={b}");
        }
        
        private void HandleModifyPaletteWithEffect(byte opcode) 
        {
            byte b = _reader.ReadByte();
            ushort w = _reader.ReadUInt16();
            byte count = _reader.ReadByte();
            var entries = new List<(byte, byte, byte, byte)>();
            for (int i = 0; i < count; i++)
            {
                entries.Add((_reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte(), _reader.ReadByte()));
            }
            _sb.AppendLine($"{_currentAddress:X8} | MODIFY_PALETTE_WITH_EFFECT B={b}, W={w}, Count={count}");
        }
        
        private void HandleExitJumpTo(byte opcode) 
        {
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "EXIT_JUMP_TO");
            _sb.AppendLine($"{_currentAddress:X8} | EXIT_JUMP_TO Target=0x{target:X4}");
        }
        
        private void HandleDisableExitMenu(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | DISABLE_EXIT_MENU");
        }
        
        private void HandleLoadJumpTo(byte opcode) 
        {
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "LOAD_JUMP_TO");
            _sb.AppendLine($"{_currentAddress:X8} | LOAD_JUMP_TO Target=0x{target:X4}");
        }
        
        private void HandleDisableLoadMenu(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | DISABLE_LOAD_MENU");
        }
        
        private void HandleClearOnJumpAddresses(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | CLEAR_ON_JUMP_ADDRESSES");
        }
        
        private void HandleJumpIfRegisterEqual(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            ushort value = _reader.ReadUInt16();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_REGISTER_EQUAL");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_REGISTER_EQUAL Reg={reg}, Value={value}, Target=0x{target:X4}");
        }
        
        private void HandleJumpIfRegisterNotEqual(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            ushort value = _reader.ReadUInt16();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_REGISTER_NOT_EQUAL");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_REGISTER_NOT_EQUAL Reg={reg}, Value={value}, Target=0x{target:X4}");
        }
        
        private void HandleJumpIfRegisterLessOrEqual(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            ushort value = _reader.ReadUInt16();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_REGISTER_LESS_THAN_OR_EQUAL");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_REGISTER_LESS_THAN_OR_EQUAL Reg={reg}, Value={value}, Target=0x{target:X4}");
        }
        
        private void HandleJumpIfRegisterGreaterOrEqual(byte opcode)
        {
            byte reg = _reader.ReadByte();
            ushort value = _reader.ReadUInt16();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_REGISTER_GREATER_THAN_OR_EQUAL");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_REGISTER_GREATER_THAN_OR_EQUAL Reg={reg}, Value={value}, Target=0x{target:X4}");
        }

        private void HandleReadRegister(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | READ_REGISTER Reg={reg}");
        }
        
        private void HandleJumpIfLastReadNotEqual(byte opcode) 
        {
            ushort value = _reader.ReadUInt16();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_LAST_READ_NOT_EQUAL"); 
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_LAST_READ_NOT_EQUAL Value={value}, Target=0x{target:X4}");
        }
        
        private void HandleWriteToMem(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            ushort value = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | WRITE_TO_MEM Reg={reg}, Value={value}");
        }
        
        private void HandleIncRegister(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | INC_REGISTER Reg={reg}");
        }
        
        private void HandleDecRegister(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | DEC_REGISTER Reg={reg}");
        }
        
        private void HandleAdd(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            ushort value = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | ADD Reg={reg}, Value={value}");
        }
        
        private void HandleOr(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            ushort value = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | OR Reg={reg}, Value={value}");
        }
        
        private void HandleAnd(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            ushort value = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | AND Reg={reg}, Value={value}");
        }
        
        private void HandleFadeToBlack(byte opcode)
        {
            ushort duration = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | FADE_TO_BLACK Duration={duration}");
        }
        
        private void HandleLoadState(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | LOAD_STATE");
        }
        
        private void HandleSaveState(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | SAVE_STATE");
        }
        
        private void HandlePaintBlackRectangle(byte opcode) 
        {
            ushort x = _reader.ReadUInt16();
            ushort y = _reader.ReadUInt16();
            ushort width = _reader.ReadUInt16();
            ushort height = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | PAINT_BLACK_RECTANGLE X={x}, Y={y}, Width={width}, Height={height}");
        }
        
		private void HandleDisplayChoiceText(byte opcode)
		{
			byte b1 = _reader.ReadByte();
			ushort w1 = _reader.ReadUInt16();
			ushort w2 = _reader.ReadUInt16();
			byte b2 = _reader.ReadByte();
			byte choiceCount = _reader.ReadByte();

			var choices = new List<string>();
			for (int i = 0; i < choiceCount; i++)
			{
				byte firstByte = _reader.PeekByte();
				long stringOffset = _reader.BaseStream.Position; // remember string start

				string text;

				if (firstByte == 0x01)
				{
					_reader.ReadByte(); // consume 0x01
					byte tokenId = _reader.ReadByte();
					text = _reader.ReadNullTerminatedString(_encoding);

					// if (_textTokenTable.TryGetValue(tokenId, out var tokenText))
						// text = tokenText + (string.IsNullOrEmpty(text) ? "" : " " + text);
				}
				else
				{
					text = _reader.ReadNullTerminatedString(_encoding);
				}

				choices.Add(text);
				AddStringMetaData(stringOffset, text, 2); // store actual string offset
			}

			_sb.AppendLine($"{_currentAddress:X8} | DISPLAY_CHOICE_TEXT B1={b1}, W1={w1}, W2={w2}, B2={b2}, ChoiceCount={choiceCount}, Choices=[{string.Join("; ", choices)}]");
		}
        
        private void HandleSetHotZoneSeparator(byte opcode) 
        {
            byte separator = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_HOT_ZONE_SEPARATOR Sep={separator}");
        }
        
        private void HandleSetSaveMenuEnabled(byte opcode) 
        {
            byte enabled = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_SAVE_MENU_ENABLED Enabled={enabled}");
        }
        
        private void HandleModifyPalette(byte opcode) 
        {
            byte count = _reader.ReadByte();
            var entries = new List<(byte idx, byte r, byte g, byte b)>();
            for (int i = 0; i < count; i++)
            {
                byte idx = _reader.ReadByte();
                byte r = _reader.ReadByte();
                byte g = _reader.ReadByte();
                byte b = _reader.ReadByte();
                entries.Add((idx, r, g, b));
            }
            _sb.AppendLine($"{_currentAddress:X8} | MODIFY_PALETTE Count={count}");
        }
        
        private void HandleWaitForInputOrPcm(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | WAIT_FOR_INPUT_OR_PCM");
        }
        
        private void HandleWordOperation(byte opcode)
        {
            ushort word = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | WORD_OPERATION Word={word}");
        }
        
        private void HandleOpcode44(byte opcode)
        {
            _sb.AppendLine($"{_currentAddress:X8} | OPCODE_44");
        }
        
        private void HandleWriteDpadDirection(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | WRITE_DPAD_DIRECTION Reg={reg}");
        }
        
        private void HandleEvaluateDpadDirection(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | EVALUATE_DPAD_DIRECTION");
        }
        
        private void HandleOpcode47(byte opcode)
        {
            byte b = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | OPCODE_47 Param={b}");
        }
        
        private void HandleJumpIfLastReadAndZero(byte opcode) 
        {
            ushort value = _reader.ReadUInt16();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_LAST_READ_AND_VALUE_EQUALS_ZERO");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_LAST_READ_AND_VALUE_EQUALS_ZERO Value={value}, Target=0x{target:X4}");
        }
        
        private void HandleWriteBufferPropertyValue(byte opcode) 
        {
            byte b1 = _reader.ReadByte();
            byte b2 = _reader.ReadByte();
            byte b3 = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | WRITE_BUFFER_PROPERTY_VALUE B1={b1}, B2={b2}, B3={b3}");
        }
        
        private void HandleLoadWin(byte opcode) 
        {
            string filename = _reader.ReadNullTerminatedString(_encoding);
            _sb.AppendLine($"{_currentAddress:X8} | LOAD_WIN File=\"{filename}\"");
        }
        
        private void HandleSetCaretPosition(byte opcode) 
        {
            ushort x = _reader.ReadUInt16();
            ushort y = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | SET_CARET_POSITION X={x}, Y={y}");
        }
        
        private void HandleVideoEffect4C(byte opcode) 
        {
            ushort w1 = _reader.ReadUInt16();
            ushort w2 = _reader.ReadUInt16();
            ushort w3 = _reader.ReadUInt16();
            ushort w4 = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | VIDEO_EFFECT_4C W1={w1}, W2={w2}, W3={w3}, W4={w4}");
        }
        
        private void HandleDisableBufferSync(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | DISABLE_BUFFER_PROPERTIES_SYNC");
        }
        
        private void HandleEnableBufferSync(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | ENABLE_BUFFER_PROPERTIES_SYNC");
        }
        
        private void HandleUpdateBufferPropertySync(byte opcode) 
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | UPDATE_BUFFER_PROPERTY_WITH_SYNC ID={id}");
        }
        
        private void HandleFadeInViewportRgb(byte opcode)
        {
            byte r = _reader.ReadByte();
            byte g = _reader.ReadByte();
            byte b = _reader.ReadByte();
            ushort duration = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | FADE_IN_VIEWPORT_RGB R={r}, G={g}, B={b}, Duration={duration}");
        }
        
        private void HandleFadeOutViewportRgb(byte opcode)
        {
            byte r = _reader.ReadByte();
            byte g = _reader.ReadByte();
            byte b = _reader.ReadByte();
            ushort duration = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | FADE_OUT_VIEWPORT_RGB R={r}, G={g}, B={b}, Duration={duration}");
        }
        
        private void HandleSetUnknownMaskState(byte opcode) 
        {
            byte state = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_MASK_STATE State={state}");
        }
        
        private void HandleRepeatJumpTo(byte opcode) 
        {
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "REPEAT_JUMP_TO");
            _sb.AppendLine($"{_currentAddress:X8} | REPEAT_JUMP_TO Target=0x{target:X4}");
        }
        
        private void HandleDisableRepeatMenu(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | DISABLE_REPEAT_MENU");
        }
        
        private void HandleSetVisiblePaletteRange(byte opcode) 
        {
            byte start = _reader.ReadByte();
            byte end = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_VISIBLE_PALETTE_RANGE Start={start}, End={end}");
        }
        
        private void HandleLoadExtendedState(byte opcode)
        {
            string filename = _reader.ReadNullTerminatedString(_encoding);
            _sb.AppendLine($"{_currentAddress:X8} | LOAD_EXTENDED_STATE File=\"{filename}\"");
        }
        
        private void HandleSaveExtendedState(byte opcode)
        {
            string filename = _reader.ReadNullTerminatedString(_encoding);
            _sb.AppendLine($"{_currentAddress:X8} | SAVE_EXTENDED_STATE File=\"{filename}\"");
        }
        
        private void HandleCopyRegToFrom(byte opcode) 
        {
            byte regTo = _reader.ReadByte();
            byte regFrom = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | COPY_REG_TO_FROM To={regTo}, From={regFrom}");
        }
        
        private void HandleDivOp(byte opcode) 
        {
            byte b = _reader.ReadByte();
            ushort w1 = _reader.ReadUInt16();
            ushort w2 = _reader.ReadUInt16();
            ushort w3 = _reader.ReadUInt16();
            ushort w4 = _reader.ReadUInt16();
            ushort w5 = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | DIV_OP B={b}, W1={w1}, W2={w2}, W3={w3}, W4={w4}, W5={w5}");
        }
        
        private void HandleUpdateBufferProperty5A(byte opcode) 
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | UPDATE_BUFFER_PROPERTY_5A ID={id}");
        }
        
        private void HandleBufferDoubleWordArrayOp(byte opcode) 
        {
            byte b1 = _reader.ReadByte();
            var dwArray = _reader.ReadDoubleWordArray();
            _sb.AppendLine($"{_currentAddress:X8} | BUFFER_PROPERTIES_DOUBLE_WORD_ARRAY_OPERATION B1={b1}, Array=[{string.Join(",", dwArray)}]");
        }
        
        private void HandleResetBufferScrollProperties(byte opcode)
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | RESET_BUFFER_SCROLL_PROPERTIES ID={id}");
        }
        
        private void HandleSetUnknownMidiProperties5D(byte opcode) 
        {
            byte value = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_MIDI_PROPERTIES_5D Value={value}");
        }
        
        private void HandleFadeOutMidi(byte opcode) 
        {
            ushort duration = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | FADE_OUT_MIDI Duration={duration}");
        }
        
        private void HandleNoOp(byte opcode) 
        {
            _sb.AppendLine($"{_currentAddress:X8} | NO_OP");
        }
        
        private void HandleFadeMidiToVolume(byte opcode)
        {
            byte volume = _reader.ReadByte();
            ushort duration = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | FADE_MIDI_TO_VOLUME Volume={volume}, Duration={duration}");
        }
        
        private void HandleSetExtendedScrollProperties(byte opcode)
        {
            byte b1 = _reader.ReadByte();
            short s1 = _reader.ReadInt16();
            short s2 = _reader.ReadInt16();
            ushort w1 = _reader.ReadUInt16();
            ushort w2 = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | SET_EXTENDED_SCROLL_PROPERTIES B={b1}, S1={s1}, S2={s2}, W1={w1}, W2={w2}");
        }
        
        private void HandleSetTextBoxState(byte opcode)
        {
            byte state = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_TEXT_BOX_STATE State={state}");
        }
        
        private void HandleSetPcmFlag(byte opcode)
        {
            byte flag = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_PCM_FLAG Flag={flag}");
        }
        
        private void HandleApplyPaletteLookupTable(byte opcode)
        {
            byte tableCount = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | APPLY_PALETTE_LOOKUP_TABLE TableCount={tableCount}");
            
            for (int i = 0; i < tableCount; i++)
            {
                ushort w = _reader.ReadUInt16();
                byte entryCount = _reader.ReadByte();
                for (int j = 0; j < entryCount; j++)
                {
                    byte b1 = _reader.ReadByte();
                    byte b2 = _reader.ReadByte();
                    byte b3 = _reader.ReadByte();
                    byte b4 = _reader.ReadByte();
                }
            }
        }
        
        private void HandleMaxCdAudioStreams(byte opcode) 
        {
            byte max = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | MAX_CD_AUDIO_STREAMS Max={max}");
        }
        
        private void HandleOpcode66(byte opcode)
        {
            byte b = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | OPCODE_66 Param={b}");
        }
        
        private void HandleEnableTextHistory(byte opcode)
        {
            _sb.AppendLine($"{_currentAddress:X8} | ENABLE_TEXT_HISTORY");
        }
        
        private void HandleAddTextHistoryEntry(byte opcode)
        {
            ushort address = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | ADD_TEXT_HISTORY_ENTRY Address=0x{address:X4}");
        }
        
		private void HandleDisplayChoiceTextWithAddresses(byte opcode)
		{
			byte b1 = _reader.ReadByte();
			ushort w1 = _reader.ReadUInt16();
			ushort w2 = _reader.ReadUInt16();
			byte choiceCount = _reader.ReadByte();

			var choices = new List<(ushort addr, string text)>();
			for (int i = 0; i < choiceCount; i++)
			{
				ushort addr = _reader.ReadUInt16();

				long stringOffset = _reader.BaseStream.Position; // capture actual string start
				string text = _reader.ReadNullTerminatedString(_encoding);

				choices.Add((addr, text));
				AddStringMetaData(stringOffset, text, 2); // store real string offset
			}

			_sb.AppendLine($"{_currentAddress:X8} | DISPLAY_CHOICE_TEXT_WITH_ADDRESSES B1={b1}, W1={w1}, W2={w2}, ChoiceCount={choiceCount}");
		}

        
        private void HandleWriteRandom(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            ushort max = _reader.ReadUInt16();
            _sb.AppendLine($"{_currentAddress:X8} | WRITE_RANDOM Reg={reg}, Max={max}");
        }
        
        private void HandleLoadPcmWithDelay(byte opcode) 
        {
            byte b1 = _reader.ReadByte();
            ushort delay = _reader.ReadUInt16();
            byte b2 = _reader.ReadByte();
            string filename = _reader.ReadNullTerminatedString(_encoding);
            _sb.AppendLine($"{_currentAddress:X8} | LOAD_PCM_WITH_DELAY B1={b1}, Delay={delay}, B2={b2}, File=\"{filename}\"");
        }
        
        private void HandleRepeatPcmInfinite(byte opcode) 
        {
            byte id = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | REPEAT_PCM_INFINITE ID={id}");
        }
        
        private void HandleJumpIfBufferPropertyNotZero(byte opcode)
        {
            byte id = _reader.ReadByte();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_BUFFER_PROPERTY_NOT_ZERO");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_BUFFER_PROPERTY_NOT_ZERO ID={id}, Target=0x{target:X4}");
        }
        
        private void HandleSetUnknownState6E(byte opcode)
        {
            byte b1 = _reader.ReadByte();
            byte b2 = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_STATE_6E B1={b1}, B2={b2}");
        }
        
        private void HandleJumpIfRegisterEqualRegister(byte opcode)
        {
            byte reg1 = _reader.ReadByte();
            byte reg2 = _reader.ReadByte();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_REGISTER_EQUAL_REGISTER");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_REGISTER_EQUAL_REGISTER Reg1={reg1}, Reg2={reg2}, Target=0x{target:X4}");
        }
        
        private void HandleJumpIfRegisterNotEqualRegister(byte opcode)
        {
            byte reg1 = _reader.ReadByte();
            byte reg2 = _reader.ReadByte();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_REGISTER_NOT_EQUAL_REGISTER");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_REGISTER_NOT_EQUAL_REGISTER Reg1={reg1}, Reg2={reg2}, Target=0x{target:X4}");
        }
        
        private void HandleJumpIfRegisterLessOrEqualRegister(byte opcode)
        {
            byte reg1 = _reader.ReadByte();
            byte reg2 = _reader.ReadByte();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_REGISTER_LESS_THAN_OR_EQUAL_REGISTER");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_REGISTER_LESS_THAN_OR_EQUAL_REGISTER Reg1={reg1}, Reg2={reg2}, Target=0x{target:X4}");
        }
        
        private void HandleJumpIfReg1GEReg2(byte opcode) 
        {
            byte reg1 = _reader.ReadByte();
            byte reg2 = _reader.ReadByte();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_REGISTER1_GREATER_THAN_OR_EQUAL_REGISTER2");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_REGISTER1_GREATER_THAN_OR_EQUAL_REGISTER2 Reg1={reg1}, Reg2={reg2}, Target=0x{target:X4}");
        }
        
        private void HandleDrawTextOnBuffer(byte opcode)
        {
            string text = _reader.ReadNullTerminatedString(_encoding);
            _sb.AppendLine($"{_currentAddress:X8} | DRAW_TEXT_ON_BUFFER Text=\"{text}\"");
        }
        
        private void HandleSetTextColor(byte opcode)
        {
            byte r = _reader.ReadByte();
            byte g = _reader.ReadByte();
            byte b = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_TEXT_COLOR R={r}, G={g}, B={b}");
        }
        
        private void HandleSetTextPosition(byte opcode)
        {
            short x = _reader.ReadInt16();
            short y = _reader.ReadInt16();
            _sb.AppendLine($"{_currentAddress:X8} | SET_TEXT_POSITION X={x}, Y={y}");
        }
        
        private void HandleShowDialog(byte opcode)
        {
            _sb.AppendLine($"{_currentAddress:X8} | SHOW_DIALOG");
        }
        
        private void HandleSetUnknownByte78(byte opcode)
        {
            byte b = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_BYTE_78 Value={b}");
        }
        
        private void HandleSetCharacterFacingFromRegister(byte opcode) 
        {
            byte reg = _reader.ReadByte();
            _sb.AppendLine($"{_currentAddress:X8} | SET_CHARACTER_FACING_FROM_REGISTER Reg={reg}");
        }
        
        private void HandleJumpIfNotZero(byte opcode)
        {
            ushort mask = _reader.ReadUInt16();
			int jumpPos = (int)_reader.BaseStream.Position;
            ushort target = _reader.ReadUInt16();
            AddJumpReference(jumpPos, target, "JUMP_IF_NOT_ZERO");
            _sb.AppendLine($"{_currentAddress:X8} | JUMP_IF_NOT_ZERO Mask=0x{mask:X4}, Target=0x{target:X4}");
        }
        
        private void HandleLoadBmp(byte opcode)
        {
            ushort offsetX = _reader.ReadUInt16();
            ushort offsetY = _reader.ReadUInt16();
            string bmpName = _reader.ReadNullTerminatedString(_encoding);
            _sb.AppendLine($"{_currentAddress:X8} | LOAD_BMP_IN_MEMORY X={offsetX}, Y={offsetY}, File=\"{bmpName}\"");
        }
		
		public void ImportText(string originalFilePath, string translationFilePath, 
                       string? outputFilePath = null, Encoding? encoding = null, int maxLineLength = 50)
		{
			encoding ??= _encoding;
			outputFilePath ??= originalFilePath;

			// Load translations
			Console.WriteLine($"Loading translations from: {translationFilePath}");
			var translations = Translation.LoadTranslations(translationFilePath);

			if (translations.Count == 0)
			{
				Console.WriteLine("ERROR: No translations found to import");
				return;
			}

			Console.WriteLine($"Found {translations.Count} translations");

			// Load original script
			Console.WriteLine($"Loading script from: {originalFilePath}");
			Load(originalFilePath, encoding);

			Console.WriteLine($"Found {_jumpReferences.Count} jump references");
			Console.WriteLine($"Found {_collectedStrings.Count} strings in script");

			// Read original file bytes
			var fileBytes = File.ReadAllBytes(originalFilePath);
			Console.WriteLine($"Original file size: {fileBytes.Length:N0} bytes");

			// Apply auto line break only to translations that differ from original
			foreach (var key in translations.Keys.ToList())
			{
				string originalText = ReadOriginalStringAt(fileBytes, key, encoding);
				if (translations[key] != originalText)
				{
					translations[key] = Translation.AutoLineBreak(translations[key], maxLineLength);
				}
			}

			// Calculate size changes
			var sizeChanges = CalculateSizeChanges(translations, fileBytes, encoding);

			if (sizeChanges.Count == 0)
			{
				Console.WriteLine("ERROR: No valid translations to apply");
				return;
			}

			Console.WriteLine($"Size changes calculated for {sizeChanges.Count} strings");

			var totalSizeChange = sizeChanges.Values.Sum();
			Console.WriteLine($"Total size change: {totalSizeChange:+0;-#} bytes");

			// Backup original bytes
			var originalFileBackup = (byte[])fileBytes.Clone();

			// Update jump references safely
			UpdateJumpReferencesSafe(fileBytes, sizeChanges);

			// Compute new file size
			var newFileSize = fileBytes.Length + totalSizeChange;
			var newFile = new byte[newFileSize];

			// Apply translations
			if (!ApplyTranslationsSegmented(fileBytes, newFile, translations, sizeChanges, encoding))
			{
				Console.WriteLine("ERROR: Failed to apply translations, restoring original");
				newFile = originalFileBackup;
			}

			// Ensure output directory exists
			var outputDir = Path.GetDirectoryName(outputFilePath);
			if (!string.IsNullOrEmpty(outputDir))
				Directory.CreateDirectory(outputDir);

			// Save translated file
			File.WriteAllBytes(outputFilePath, newFile);

			Console.WriteLine($"Saved translated script: {outputFilePath}");
			Console.WriteLine($"   Original size: {fileBytes.Length:N0} bytes");
			Console.WriteLine($"   New size: {newFile.Length:N0} bytes");
			Console.WriteLine($"   Size change: {(newFile.Length - fileBytes.Length):+0;-#} bytes");
		}


		private Dictionary<long, int> CalculateSizeChanges(Dictionary<long, string> translations, byte[] fileBytes, Encoding encoding)
		{
			var sizeChanges = new Dictionary<long, int>();

			foreach (var kvp in translations)
			{
				long address = kvp.Key;
				string translatedText = kvp.Value;

				if (address >= fileBytes.Length)
				{
					Console.WriteLine($"WARNING: Address 0x{address:X8} beyond file bounds, skipping");
					continue;
				}

				string originalText = ReadOriginalStringAt(fileBytes, address, encoding);
				if (translatedText == originalText) 
					continue;

				int originalSize = encoding.GetByteCount(originalText) + 1;

				byte[] translatedBytes;
				try
				{
					translatedBytes = encoding.GetBytes(translatedText);
				}
				catch
				{
					Console.WriteLine($"ERROR: Cannot encode translated string at 0x{address:X8}");
					continue;
				}

				int translatedSize = translatedBytes.Length + 1;
				sizeChanges[address] = translatedSize - originalSize;
			}

			return sizeChanges;
		}

		private bool ApplyTranslationsSegmented(byte[] sourceBytes, byte[] targetBytes, 
				Dictionary<long, string> translations, Dictionary<long, int> sizeChanges, Encoding encoding)
		{
			int sourcePos = 0;
			int targetPos = 0;

			var sortedAddresses = sizeChanges.Keys.OrderBy(x => x).ToList();

			foreach (var addr in sortedAddresses)
			{
				if (addr >= sourceBytes.Length)
					continue;

				int copyLen = (int)(addr - sourcePos);
				if (copyLen > 0)
				{
					Array.Copy(sourceBytes, sourcePos, targetBytes, targetPos, copyLen);
					sourcePos += copyLen;
					targetPos += copyLen;
				}

				if (!translations.TryGetValue(addr, out var translatedText))
				{
					Console.WriteLine($"WARNING: Translation missing for 0x{addr:X8}");
					translatedText = ReadOriginalStringAt(sourceBytes, addr, encoding);
				}

				byte[] translatedBytes = encoding.GetBytes(translatedText);

				if (targetPos + translatedBytes.Length + 1 > targetBytes.Length)
				{
					Console.WriteLine($"ERROR: Target buffer overflow at 0x{addr:X8}");
					return false;
				}

				Array.Copy(translatedBytes, 0, targetBytes, targetPos, translatedBytes.Length);
				targetPos += translatedBytes.Length;
				targetBytes[targetPos++] = 0x00;

				sourcePos += GetOriginalStringLength(sourceBytes, (int)addr);
			}

			int remaining = sourceBytes.Length - sourcePos;
			if (remaining > 0)
				Array.Copy(sourceBytes, sourcePos, targetBytes, targetPos, remaining);

			return true;
		}

		private string ReadOriginalStringAt(byte[] fileBytes, long address, Encoding encoding)
		{
			int start = (int)address;
			int end = Array.IndexOf(fileBytes, (byte)0x00, start);
			if (end < 0) end = fileBytes.Length; // No null terminator found

			int length = end - start;
			return encoding.GetString(fileBytes, start, length);
		}


		private int GetOriginalStringLength(byte[] fileBytes, int address)
		{
			int endIndex = Array.IndexOf(fileBytes, (byte)0x00, address);
			return (endIndex >= 0) ? endIndex - address + 1 : fileBytes.Length - address;
		}

		private void UpdateJumpReferencesSafe(byte[] fileBytes, Dictionary<long, int> sizeChanges)
		{
			if (_jumpReferences.Count == 0 || sizeChanges.Count == 0)
				return;

			var sortedAddresses = sizeChanges.Keys.OrderBy(x => x).ToList();
			var cumulative = new Dictionary<long, int>();
			int sum = 0;
			foreach (var addr in sortedAddresses)
			{
				sum += sizeChanges[addr];
				cumulative[addr] = sum;
			}

			int GetOffset(int targetAddr)
			{
				int offset = 0;
				foreach (var addr in sortedAddresses)
				{
					if (addr < targetAddr)
						offset = cumulative[addr];
					else
						break;
				}
				return offset;
			}

			foreach (var jump in _jumpReferences)
			{
				if (jump.Address < 0 || jump.Address + 2 > fileBytes.Length)
					continue;

				ushort target = BitConverter.ToUInt16(fileBytes, jump.Address);
				int offset = GetOffset(target);
				if (offset == 0) continue;

				int newTarget = target + offset;
				if (newTarget < 0 || newTarget > ushort.MaxValue)
					continue;

				BitConverter.GetBytes((ushort)newTarget).CopyTo(fileBytes, jump.Address);
			}
		}
		
		public void ExportText(string filePath)
		{
			if (_collectedStrings == null || _collectedStrings.Count == 0)
			{
				Console.WriteLine("No strings to export.");
				return;
			}

			int exportedCount = 0;

			using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false)); // UTF-8 without BOM

			foreach (var meta in _collectedStrings)
			{
				// Skip invalid types or empty text
				if (meta.Text is null or "" || (meta.Type != 0 && meta.Type != 1 && meta.Type != 2))
					continue;

				string text = meta.Text.EscapeText();
				string name = string.IsNullOrEmpty(meta.NamePrefix)
					? string.Empty
					: $"|{meta.NamePrefix.EscapeText()}|";

				writer.WriteLine($"◇{meta.Address:X8}◇{name}{text}");
				writer.WriteLine($"◆{meta.Address:X8}◆{name}{text}");
				writer.WriteLine();

				exportedCount++;
			}

			Console.WriteLine($"Exported {exportedCount} string(s) to {Path.GetFileName(filePath)}");
		}
    }
}
