#!/bin/bash

if [ -z ${1} ]; then
  # no version number passed in, get last release tag from github
  release_tag=`git describe --abbrev=0`
else
  # can pass a target release tag e.g. bash tools/contributors.sh v1.0.2
  # must include the "v" at the front of the version number e.g. "v1.0.2" NOT "1.0.2"
  release_tag=$1
fi

function report_contributions {
  echo `count_contributors` contributors since release $release_tag
  printf "\nTOTAL\tADDED\tREMOVED\tAUTHOR\n"
  print_contributions
}

function print_contributions {
  git log $release_tag..HEAD --oneline --numstat --pretty=format:%an --no-merges --abbrev-commit | awk 'author == "" { author = $0; next } /^$/ { author = ""; next} {added[author] += $1; removed[author] +=$2 } END { for(author in added) { printf "%s\t%s\t%s\t%s\n", added[author]+removed[author], added[author], removed[author], author } }' | sort -n -k1 -r
}

function count_contributors {
  print_contributions | wc -l
}


report_contributions