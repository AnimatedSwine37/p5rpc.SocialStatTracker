# p5rpc.SocialStatTracker

Displays how many more points you need to rank up each social stat in the stats menu and when gaining stats. (Also on [GameBanana](https://gamebanana.com/mods/414790)!)

Note that the number of notes shown when getting points towards a social stat does not equal the number of actual points gained. It is grouped as follows:
- 1 Note = 1-2 points
- 2 Notes = 3-4 points
- 3 Notes = 5+ points

Using this mod you'll be able to see exactly how close you are to leveling up a stat and even see how many points over the max level you are. Having points over the max level doesn't actually accomplish anything in-game but it is a fun bit of information to see :)

Also, for anyone curious, the maximum number of points you can get for each stat is 32767 after which it will become negative and you'll go back to level 1 (the points are stored in 2 signed bytes). To overflow back into positives you'd need another 32768 points. There seems to be no check for this so technically you could do it naturally although it would require a stupid amount of new game pluses :)
