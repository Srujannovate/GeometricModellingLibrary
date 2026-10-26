# M10 screw 2D drawing schema
# Matches the reference drawing: hex head on the left, shaft on the right,
# with top dimension line and central guide line.

# Top overall dimension line with end ticks
create line 0,25,0 58,25,0
create line 0,20,0 0,30,0
create line 58,20,0 58,30,0

# Hex head outline (front view)
create line 0,0,0 7,10,0
create line 7,10,0 18,10,0
create line 18,10,0 24,0,0
create line 24,0,0 18,-10,0
create line 18,-10,0 7,-10,0
create line 7,-10,0 0,0,0

# Shaft body as a rectangle
create line 24,-6,0 24,6,0
create line 24,6,0 54,6,0
create line 54,6,0 54,-6,0
create line 54,-6,0 24,-6,0

# Shaft centerline and reference line
create line 24,0,0 54,0,0
create line 40,-12,0 40,12,0
create line 38,-12,0 42,-12,0
create line 38,12,0 42,12,0

# Left-side guide and alignment line
create line 0,0,0 0,-18,0
create line 0,-18,0 24,-18,0
create line 24,-18,0 24,-6,0

# Small top guide line from head center
create line 12,0,0 12,14,0
create line 12,14,0 24,14,0
