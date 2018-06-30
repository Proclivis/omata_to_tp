# omata_to_tp
Translates an Omata Fit file to a Traningpeaks Fit file with power data.

## Why?
Omata can upload data to Strava, and Strava will calculate power. But it cannot upload to TrainingPeaks, 
and exporting data from Strava does not include power data. If you have multiple bikes, some with power,
and some without, and you want to keep your TSS up to date for the non power bike, and you use
and Omata, this is intended to help.

## Accuracy
I tested once with a Quark, and by by tuning the drafting, got about 20% accurate. Power is
very sensitive to your size, and wind. Don't expect perfection.

## Use model
Ride, download to phone, share to file system, run translate, manually upload to TrainingPeaks.

## The Math
The math equations come from Validation of a Mathemstical Model for Road Cycling Power. However,
some things are not used, such as altitude, temperature, wind, etc. The constants that are used 
are exposed through the command line.

Type translate --help to get a list of options. They have reasonable defaults.

## How to use
Call on the command line. On a Mac, modo translate --infile=in.fit --outfile=out.fit

Be sure to look at the option for drafting. If you take some data from a power meter
and a good bike computer along with the Omata, you can kind of figure out what value
gives you a reasonable correlation.

## Omata data issues
The Omata has a couple of fit file things you need to know. First, there are two
record descriptors, one has timestamp, the other doesnt, but all data records have them. 
Second, the math has division by subtracted values, so there is some protection against this.
Finally, due to limits of resolution, there are delta values of zero. Since the tool works
by adding power to all records, if there is no useful delta, the last power is used.
Otherwise, the TSS will not be correct without deleting records, which this does not do.
