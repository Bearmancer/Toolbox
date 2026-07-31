Part III. Converting The Image Rip: Hide

Part III. Converting The Image Rip

You should now have a full .ISO image of your SACD. If you downloaded an image from themusichere.in, RuTracker or the like, and just want to convert, start here. To convert the image to a rippable standard, a number of things must be accomplished;

1 - Convert the image into a DSD format audio file(s)
2 - Determine the gain that must be applied to the file(s) when converting to PCM
3 - Perform the conversion to PCM
4 - Split (if appropriate) and tag

Before I go into the process of doing these steps manually, I will note that there is a script to do many of these functions automatically based off an ISO image, thanks to the users at the SACD forum and posted here by Faunts.

info.txt: Hide
To install this script (toolkit, actually), please extract the contents of the archive (preserving
the directory structure) to an empty folder (named with a short name, e.g. "S2F") on your hard
disk with a lot of free space.
The archive comes with the pre-configured portable Foobar2000, located in the subfolder "Programs",
so there is no need to install foobar200 separately.

Then change (if needed) in the 4th line of the MCHsacdISOs2FLACs.bat the path to the installation
directory of your Saracon.

Then copy one or several SACD ISOs into this folder (where the .bat files are located) and start
MCHsacdISOs2FLACs.bat, or 2CHsacdISOs2FLACs.bat, or MCH_and_2CHsacdISOs2FLACs.bat.

That script extracts (even if the iso's filename contains "&", "!", diacritical or unicode symbols)
the .dff files, finds correct (clipping-free) gain (not greater then 6 dB),
converts these .dffs with this gain into temporary 88.2kHz PCM .caf format, and finally
converts these temporary files into the flacs (with trimming of the clicks at the beginning and at
the end of each .flac file).
Then this script will tag these flacs with the information from sacd.iso, keeping untouched all "&"
and "!" in the "Artist" and "Title" tags. Finally,the script writes a .Log file with debugging
information.

The resulting .flac files can be found in the newly created subdirectory(-ies) (named after the
SACD titles).


The script is available here, and comes with all its dependencies. It's not designed to be run on any platforms other than Windows; you may have luck with Wine, but I tried on Mac and couldn't get it to work. Nevertheless, it's still possible to extract SACDs correctly on Mac and Linux manually. Direct all support queries for this script to Faunts.

Now onto the manual process.

Process 1: Using SACD_extract

A CLI tool called sacd_extract is capable of handling the disc image and converting it into a DSD file.

Sonore provides a tool called ISO2DSD, available here, which is a cross-platform Java GUI wrapper for sacd_extract that makes it somewhat easier to use.

Command Line Options Explained: Hide

The following options are available for the sacd_extract command line tool:

Usage: sacd_extract [options] [outfile]
-2, --2ch-tracks: Export two channel tracks (default)
-m, --mch-tracks: Export multi-channel tracks
-e, --output-dsdiff-em: output as Philips DSDIFF (Edit Master) file
-p, --output-dsdiff: output as Philips DSDIFF file
-s, --output-dsf: output as Sony DSF file
-I, --output-iso: output as RAW ISO
-c, --convert-dst: convert DST to DSD
-C, --export-cue: Export a CUE Sheet
-i, --input[=FILE]: set source and determine if "iso" image,
device or server (ex. -i192.168.1.10:2002)
-P, --print: display disc and track information

Help options:
-?, --help: Show this help message
--usage: Display brief usage message
[/hide]
Usage examples:

Extract all stereo tracks to multiple DSDIFF files and convert all DST to DSD:

sacd_extract -2 -p -c -i"Foo_Bar_RIP.ISO"

Extract all multi channel tracks from the given ISO to multiple DSF files and convert all DST to DSD:

sacd_extract -2 -s -i"Foo_Bar_RIP.ISO"

Extract a single DSDIFF/DSD Multi-Channel Edit Master track from the given ISO and convert all DST to DSD:

sacd_extract -m -e -c -i"Foo_Bar_RIP.ISO"

Extract a single DSDIFF/DSD Stereo Edit Master track from the given ISO, create a CUE file, and convert all DST to DSD:

sacd_extract -2 -e -c -C -i"Foo_Bar_RIP.ISO"

Extract a single ISO file from the SACD Ripper Daemon (IP address and Port is displayed on startup). You can use SACD Extract again on the ISO file to extract the DSD data (see the four examples above):

sacd_extract -I -i192.168.2.10:2002

Extract all multi channel tracks from the SACD Ripper Daemon (IP address and Port is displayed on startup) to multiple DSDIFF files and keep the DST format:

sacd_extract -m -p -i192.168.2.10:2002

Generate a sacd_log.txt file that contains the ISRC codes which should/could be used for ISO verification.

sacd_extract -P -i192.168.2.10:2002 >sacd_log.txt


As you can probably see when looking through the CLI arguments, there are two possible formats for output worth mentioning - DSDIFF and DSDIFF Edit Master. The former option will output as a number of split tracks, and the latter will output as a single track with cue file. With the former option, you won't have to split the tracks manually later using the cue. However, the DSD->PCM conversion process leaves a clicking sound at the beginning of each track, meaning that the tracks must be trimmed using SoX (or similar) to remove it - ripping as a single track, converting to PCM, then splitting using the cue in the PCM domain makes this trimming process unnecessary. It's a matter of personal preference.

A user has created a Python script to sanitise DSDIFF (a.k.a. DFF) files, because sacd_extract, like Philips' ProTECH DSTEncoder, writes ID3 tags to created files. Rule 2.14.1.5.2 reads
DSD audio in the DFF container type will be deleted if it has ID3 tags, Vorbis Comments, or any other metadata in the audio files.

The script can be located here: Sanitizing DFF Files For Upload

Process 2: Checking Gain

SACDs are mastered such that when converted to PCM they are approximately 6dB too quiet. However, different SACDs are mastered differently - as such, if you don't check the track peaks before converting, you may discover that you have added too much gain and induced clipping in some tracks, which is clearly undesirable.

There are a number of ways to measure this clipping. A popular method is using the Dynamic Range (DR Meter) plugin for foobar2000, coupled with the foo_input_sacd plugin allowing foobar2000 to read DSD files. These latest version of these plugins are available below:
foo_input_sacd plugin.
foo_dr_meter plugin.

Once you've installed the plugins, drag and drop the DSD file(s) to the GUI.

https://redacted.sh/i/gb5LLPeLU5Y.png

Select them all, right click and open the Dynamic Range Meter, which will calculate the dynamic range but also the greatest peak across all tracks (since the album is gained as a whole, without per-track normalisation, the gain value for all tracks must not cause clipping at the highest point of the highest track). Once the meter is finished, you should get a result something like this;

https://redacted.sh/i/tvM2qJCkdp0.png

As you can see, the maximum peak of this album is -4.24dB below the maximum. As such, if we had applied the standard +6dB of gain we would have induced clipping at some point in the album. Of course, there are SACDs where +6dB of gain (or above!) won't cause clipping, but you should always check.

Foobar2000 and the appropriate plugins is confirmed working on Mac with Wine and probably works on Linux too. Any other method to check the peaks of an album can be used. However, if the method you've chosen doesn't support the input of DSD files then you'll have to convert the file to WAV with 0dB gain, check the peaks and convert again with the correct gain, which is a time consuming process.

Process 3: Converting DSD to PCM

Many tools are capable of the necessary resampling. However, given that audio resampling is more of an art than a science, the only tool currently recommended is Weiss Saracon.

Select "DSD to PCM," and press "Edit" to configure the conversion.
https://redacted.sh/i/y5M12YV5VM0.png

Place your file(s) in and configure like so.

https://redacted.sh/i/7_q62iRc5bY.png

- Normally WAV is the best choice for the format, however you can pick FLAC here if you want to encode directly to FLAC. If you're converting a multichannel SACD and you output it as a single track and cue earlier, select "Sonic Foundry 64bit Wave," as normal WAV has a file size limit of 4Gb.

- Set the bit depth at 24 bit.

- Leave the dithering on the standard TPDF algorithm.

- Set the sample rate to 88.2kHz.

- Set the gain to something below the peak you observed earlier. In this case, I have gained to 1dB below the peak, but realisticall somewhere between 0.5-0.3 below the peak is sensible. Leave a little headroom.

Once you've converted the file(s) to PCM, if you ripped as individual files you'll have to trim the files by 0.0065s using to take the static "click" noise off. You can use Saracon or other software such as Audition or Audacity.

This sox command will do the job:
sox <in.flac> <out.flac> trim 0.0065 reverse silence 1 0 0% trim 0.0065 reverse pad 0.0065 0.2

After the conversion, copy the complete log out and paste it into a notepad, save it as a LOG file, include the PS3 log file as well.

If you converted with a single file and cue, open the cue up in a text editor and change it to specify the WAV file. Then you can use any standard program to split the WAV. I use XLD on Mac, but a number of CLI and GUI tools will accomplish this.

Depending on the original SACD and the conversion processes used, the resulting files may have some or no tags. Open them in a tag editor and tag them properly before uploading.