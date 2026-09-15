# get data for FSR_RECs
# hmi.phasemaps_corrected[4875802,14171657,23845068,32869398,51564722,65714753,86968067,104314890,121048472][][][]

foreach f (`ls hmi.phasemaps_corrected.*.2.128.phases.fits`)
  set g=`echo $f | sed "s/........_......_TAI\.2\.128\.//"`
  echo $g
  /bin/cp -pf $f $g
end

