/*
	Icod.ProcPs.Shared.Tests
	Tests for shared ProcPs process and system observation components.
	Copyright (C) 2026  Timothy J. Bruce <uniblab@hotmail.com>
*/

/*
	This program is free software: you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation, either version 3 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

namespace Icod.ProcPs.Shared.Tests;

using Icod.ProcPs.Shared;
using System.Text;
using Xunit;

/// <summary>Contains tests for linux proc parsers.</summary>
public sealed class LinuxProcParsersTests {
	/// <summary>Verifies that process stat preserves right parenthesis in command name.</summary>
	[Fact]
	public void ProcessStatPreservesRightParenthesisInCommandName() {
		var record = LinuxProcParsers.ParseProcessStat( "123 (weird ) name) S 1 123 123 0 456 0 0 0 0 0 10 20 0 0 20 5 3 0 777 4096 2" );
		Assert.Equal( "weird ) name", record.CommandName );
		Assert.Equal( 1, record.ParentProcessId );
		Assert.Equal( 123, record.ProcessGroupId );
		Assert.Equal( 456, record.TerminalForegroundProcessGroupId );
		Assert.Equal( 10UL, record.UserCpuTicks );
		Assert.Equal( 20UL, record.SystemCpuTicks );
		Assert.Equal( 5, record.NiceValue );
		Assert.Equal( 3, record.ThreadCount );
		Assert.Equal( 777UL, record.StartTimeTicks );
		Assert.Equal( 4096UL, record.VirtualMemoryBytes );
		Assert.Equal( 2L, record.ResidentSetPages );
	}

	/// <summary>Verifies that status parses identity and pid namespace columns.</summary>
	[Fact]
	public void StatusParsesIdentityAndPidNamespaceColumns() {
		var record = LinuxProcParsers.ParseProcessStatus( "Name:\tfixture\nUid:\t1000\t1001\t1002\t1003\nGid:\t2000\t2001\t2002\t2003\nNSpid:\t42\t7\n" );
		Assert.Equal( 1000U, record.RealUserId );
		Assert.Equal( 1001U, record.EffectiveUserId );
		Assert.Equal( 2000U, record.RealGroupId );
		Assert.Equal( 2001U, record.EffectiveGroupId );
		Assert.Equal( new[] { 42, 7 }, record.NamespaceProcessIds );
	}

	/// <summary>Verifies that null delimited utf8 preserves arguments.</summary>
	[Fact]
	public void NullDelimitedUtf8PreservesArguments() {
		var bytes = Encoding.UTF8.GetBytes( "command\0two words\0--flag\0" );
		Assert.Equal( new[] { "command", "two words", "--flag" }, LinuxProcParsers.ParseNullDelimitedUtf8( bytes ) );
	}

	/// <summary>Verifies that mem info converts kibibytes to bytes.</summary>
	[Fact]
	public void MemInfoConvertsKibibytesToBytes() {
		var values = LinuxProcParsers.ParseMemInfo( "MemTotal: 2 kB\nHugePages_Total: 4\nHugepagesize: 2048 kB\n" );
		Assert.Equal( 2048UL, values[ "MemTotal" ] );
		Assert.Equal( 4UL, values[ "HugePages_Total" ] );
		Assert.Equal( 2UL * 1024UL * 1024UL, values[ "Hugepagesize" ] );
	}

	/// <summary>Verifies that memory map parser retains path with spaces.</summary>
	[Fact]
	public void MemoryMapParserRetainsPathWithSpaces() {
		var entry = LinuxProcParsers.ParseMemoryMapLine( "00400000-00452000 r-xp 00000000 08:02 123 /tmp/file with spaces" );
		Assert.Equal( 0x00400000UL, entry.StartAddress );
		Assert.Equal( 0x00452000UL, entry.EndAddress );
		Assert.Equal( "/tmp/file with spaces", entry.Path );
	}
}
