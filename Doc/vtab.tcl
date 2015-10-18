###############################################################################
#
# vtab.tcl --
#
# Written by Joe Mistachkin.
# Released to the public domain, use at your own risk!
#
###############################################################################

proc readFile { fileName } {
  set file_id [open $fileName RDONLY]
  fconfigure $file_id -encoding binary -translation binary
  set result [read $file_id]
  close $file_id
  return $result
}

proc writeFile { fileName data } {
  set file_id [open $fileName {WRONLY CREAT TRUNC}]
  fconfigure $file_id -encoding binary -translation binary
  puts -nonewline $file_id $data
  close $file_id
  return ""
}

proc englishToList { value } {
  set result [list]

  foreach element [split $value "\t\n ,"] {
    if {[string tolower $element] ni [list "" and or]} then {
      lappend result $element
    }
  }

  return $result
}

proc processLine { line } {
  if {[string length [string trim $line]] == 0 || \
      [regexp -- {<h\d(?: |>)} [string range $line 0 3]]} then {
    return ""
  }

  set result $line

  foreach remove [list \
      {<a name=".*?">} {<a href=".*?">} {</a>} {<b>} {</b>} \
      {<dd>} {</dd>} {<dl>} {</dl>} {<dt>} {</dt>} {<li>} \
      {</li>} {<ol>} {</ol>} {<p>} {</p>} {<ul>} {</ul>}] {
    regsub -all -- $remove $result "" result

    if {[string length [string trim $result]] == 0} then {
      puts "STOP with no content, original line = $line"
      return ""
    }
  }

  regsub -all -- {<br>} $result \n result
  regsub -all -- {&ne;} $result {\&#8800;} result
  regsub -all -- {&#91(?:;)?} $result {[} result
  regsub -all -- {&#93(?:;)?} $result {]} result
  regsub -all -- {<( |\"|\d|=)} $result {\&lt;\1} result
  regsub -all -- {( |\"|\d|=)>} $result {\1\&gt;} result
  regsub -all -- {<blockquote><pre>} $result <code> result
  regsub -all -- {</pre></blockquote>} $result </code> result
  regsub -all -- {<blockquote>} $result <code> result
  regsub -all -- {</blockquote>} $result </code> result

  return $result
}

proc extractMethod { name lines pattern indexVarName methodsVarName } {
  upvar 1 $indexVarName index
  upvar 1 $methodsVarName methods

  set paragraph 0
  set length [llength $lines]

  while {$index < $length} {
    set line [lindex $lines $index]

    if {[regexp -- $pattern $line]} then {
      break; # stop on this line for outer loop.
    } else {
      set trimLine [string trim $line]; set data ""

      if {$paragraph > 0 && [string length $trimLine] == 0} then {
        # blank line, close paragraph.
        append data \n </para>; incr paragraph -1
      } elseif {[string range $trimLine 0 2] eq "<p>"} then {
        # open paragraph ... maybe one line?
        if {[string range $trimLine end-3 end] eq "</p>"} then {
          set newLine [processLine $line]

          if {[string length $newLine] > 0} then {
            # one line paragraph, wrap.
            if {[info exists methods($name)]} then {
              append data \n
            }

            append data <para> \n $newLine \n </para>
          }
        } else {
          if {[info exists methods($name)]} then {
            append data \n
          }

          append data <para>

          set newLine [processLine $line]

          if {[string length $newLine] > 0} then {
            # open paragraph, add line to it.
            append data $newLine
          }

          incr paragraph
        }
      } else {
        set newLine [processLine $line]

        if {[string length $newLine] > 0} then {
          if {[info exists methods($name)]} then {
            append data \n
          }

          append data $newLine
        }
      }

      if {[string length $data] > 0} then {
        append methods($name) $data
      }

      incr index; # consume this line for outer loop.
    }
  }
}

#
# NOTE: This is the entry point for this script.
#
set path [file normalize [file dirname [info script]]]

set coreDocPath [file join $path Extra Core]
set interfacePath [file join [file dirname $path] System.Data.SQLite]

set inputFileName [file join $coreDocPath vtab.html]
set outputFileName [file join $interfacePath ISQLiteNativeModule.cs]

set methodPattern {^<h3>2\.\d+ The (.*) Method(?:s)?</h3>$}
set start false
array set methods {}
set lines [split [readFile $inputFileName] \n]
set length [llength $lines]

for {set index 0} {$index < $length} {} {
  set line [lindex $lines $index]

  if {$start} then {
    if {[regexp -- {^<a name=".*"></a>$} $line]} then {
      incr index; continue
    }

    if {[regexp -- $methodPattern $line dummy capture]} then {
      foreach method [englishToList $capture] {
        set methodIndex [expr {$index + 1}]
        extractMethod $method $lines $methodPattern methodIndex methods
      }

      set index $methodIndex
    } else {
      incr index
    }
  } elseif {[regexp -- {^<h2>2\.0 Virtual Table Methods</h2>$} $line]} then {
    set start true; incr index
  } else {
    incr index
  }
}

# "        /// <summary>"
# "        /// </summary>"
# exit 0
