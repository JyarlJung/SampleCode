using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

/* 
GlobalClass매크로를 사용하면 엔진의 내부 Class처럼 사용할 수있습니다.
Node3D는 유니티의 MonoBehaviour와 비슷한 기능을 가진 Class입니다.
*/
[GlobalClass]
public partial class Collider : Node3D
{
    private static int MAX_LAYER=32;

	
    /*
	유니티의 레이어 기반 충돌과 비슷하게_layer, _collision멤버로 인스턴스가 속한 레이어를 설정합니다.
	[Export] 매크로는 유니티 Inspector에 나타나는 멤버 변수처럼 에디터에서 편집할 수 있게 해줍니다.
	*/
	//Member
    [Export]
    private Array<Vector3> _points=new();
    [Export(PropertyHint.Layers3DPhysics)]
    private int _layer=1, _collision=1;
    [Export]
    private Node3D _rootNode;
    private Vector4 _rectHull=Vector4.Zero;
    private float _maxRadius=0.1f;
	private static List<List<Collider>> colliders= new();
    private List<Segment> _segments = new();

    //Property
    [Export]
    public float Width=0;
    [Export]
    public bool UseRectHull=false;//Rotation이 자주 일어나는 경우 원형, 그렇지 않은 경우 사각형 Hull을 사용합니다.

    //MemberGetter
    public Node3D RootNode { get => _rootNode;}
    public List<Segment> Segments { get => _segments;}

	//Enable이 false인 선분의 경우 충돌 체크를 하지 않습니다.
	public class Segment
	{
		//Property
		public Vector3 Pos;
		public bool Enable = true;
		public Segment(Vector3 pos, bool enable)
		{
			Pos=pos;
			Enable=enable;
		}
	}
	//충돌시 정보를 담아두는 클래스입니다.
	public class Collision
	{
		//Member
		private int _index;
		private Collider _target;
		private int _targetIndex;
		private Vector3 _position;
		//MemberGetter
		public int Index { get => _index;}
		public Collider Target { get => _target;}
		public int TargetIndex { get => _targetIndex;}
		public Vector3 Position { get => _position;}
		
		public Collision(int index, Collider target, int targetIndex, Vector3 position)
		{
			_index=index;
			_target=target;
			_targetIndex=targetIndex;
			_position=position;
		}

	}
    
	/*
	첫 인스턴스가 생성된 경우, 인스턴스를 레이어별로 분류하는 List를 생성합니다.
	에디터에서 설정한 위치들을 선분 클래스로 변환합니다.
	그 후 최초로 Convex Hull을 계산합니다.
	*/
	public override void _Ready()
    {
		if(colliders.Count<MAX_LAYER)InitColliderGroup();
        AddInCollisionLayer(this);
        foreach(Vector3 pos in _points)
        {
            this._segments.Add(new Segment(pos,true));
        }
		SetConvexHull();
    }
    public override void _ExitTree()
    {
        RemoveAtCollisionLayer(this);
    }

	/*
	HitTest의 경우 오브젝트와 오브젝트를 직접 충돌검사합니다. 
	HitTestFirst, HitTestAll의 경우 설정된 충돌 레이어에 따라 자동으로 충돌을 검사합니다.
	*/
	public Collision HitTest(Collider obj)
	{
        if(!HitTestConvexHull(obj)) return null;
		Collision collision = null;
		for(int i = 0; i < Segments.Count; i++)
		{
			 if(!Segments[i].Enable)continue;

			for(int j = 0; j < obj.Segments.Count; j++)
			{
				if(!obj.Segments[i].Enable)continue;

				collision = SegmentToSegment(i,obj,j);
				if(collision != null) return collision;
			}
		}
		return null;
	}
	public Collision HitTestFirst(int mask = -1)
	{
        if(!IsVisibleInTree()) return null;
		if(mask==-1)mask=_collision;

		foreach(int i in GetBitFlagsList(mask))
		{
			foreach(Collider obj in colliders[i])
			{
				if(this.Equals(obj) || !obj.IsVisibleInTree())continue;
                Collision collision = HitTest(obj);
                if(collision!=null)return collision;
			}
		}
		return null;
	}
	public List<Collision> HitTestAll(int mask = -1)
	{
        if(!IsVisibleInTree()) return null;
		List<Collision> res=new();

		if(mask==-1)mask=_collision;

		foreach(int i in GetBitFlagsList(mask))
		{
			foreach(Collider obj in colliders[i])
			{
				if(this.Equals(obj) || !obj.IsVisibleInTree())continue;
                Collision collision = HitTest(obj);
                if(collision!=null)res.Add(collision);
			}
		}
		return res;
	}

	/*
	런타임 도중에 선분을 추가할 수 있습니다. 그 경우 ConvexHull을 다시 계산합니다.
	*/
	public void AddSegment(Vector3 pos, bool enable = true)
    {
        this._segments.Add(new Segment(pos,enable));
		SetConvexHull();
    }
	public void SetSegment(int index,Vector3 pos)
    {
        _segments[index].Pos=pos;
		SetConvexHull();
    }
    public void SetSegment(int index,Vector3 pos, bool set)
    {
        _segments[index].Pos=pos;
        _segments[index].Enable=set;
		SetConvexHull();
    }
    public void SetSegment(int index,bool set = true)
    {
        _segments[index].Enable=set;
		SetConvexHull();
    }

    public Vector3 GetPos(int index)
    {
        return _segments[index].Pos;
    }
    public bool GetEnable(int index)
    {
        return _segments[index].Enable;
    }
   
	/*
	인스턴스가 생성되고 제거될때, 충돌 레이어에 추가하고 빼주는 함수입니다.
	*/
	private static void AddInCollisionLayer(Collider node)
	{
		foreach(int i in GetBitFlagsList(node._layer))
		{
			colliders[i].Add(node);
		}
	}
	private static void RemoveAtCollisionLayer(Collider node)
	{
		foreach(int i in GetBitFlagsList(node._layer))
		{
			colliders[i].Remove(node);
		}
	}

	/*
	첫 인스턴스가 생성될때 호출되는 충돌 레이어 설정입니다.
	*/
    private static void InitColliderGroup()
    {
        for(int i=0; i<MAX_LAYER;i++)
        {
            colliders.Add(new List<Collider>());
        }
    }

	/*
	선분들의 Position은 Local하게 표현됩니다. 이를 Global한 좌표로 변환해주는 함수입니다.
	*/
    private static Vector3 GetSegmentGlobalPosition(Collider obj, int segmentIndex)
    {
		Vector3 res = obj._segments[segmentIndex].Pos.Rotated(Vector3.Back,obj.GlobalRotation.Z) + obj.GlobalPosition;
        return res;
    }

	/*
	0001011과 같은 bitflag를 [0,1,3]으로 변환해주는 함수힙니다.
	*/
	public static List<int> GetBitFlagsList(int bitflags)
	{
		List<int> res = new List<int>();
		int index = 0;
		while( bitflags > 0 && index <=MAX_LAYER)
		{
			if((bitflags & 1) != 0)res.Add(index);
			index++;
			bitflags >>= 1;
		}
		return res;
	}

	/*
	HitTest종류의 함수가 본격적으로 충돌검사를 하기 전 호출되는 ConvexHull 충돌검사입니다. True를 반환한 경우에만 선분간 충돌체크를 시작합니다.
	*/
    private bool HitTestConvexHull(Collider obj)
    {
        Vector3 objpos=obj.GlobalPosition;
        if(UseRectHull && obj.UseRectHull)
        {
            if(objpos.X+obj._rectHull.X>GlobalPosition.X+_rectHull.Z && objpos.X>GlobalPosition.X)return false;
            if(objpos.X+obj._rectHull.Z<GlobalPosition.X+_rectHull.X && objpos.X<=GlobalPosition.X)return false;
            if(objpos.Y+obj._rectHull.Y>GlobalPosition.Y+_rectHull.W && objpos.Y>GlobalPosition.Y)return false;
            if(objpos.Y+obj._rectHull.W<GlobalPosition.Y+_rectHull.Y && objpos.Y<=GlobalPosition.Y)return false;
        }
        else if(!UseRectHull && !obj.UseRectHull)
        {
            if(GlobalPosition.DistanceTo(obj.GlobalPosition)>_maxRadius+obj._maxRadius)return false;
        }
        else if(!UseRectHull)
        {
            float dx=Math.Max(objpos.X+(obj._rectHull.X)-GlobalPosition.X,GlobalPosition.X-(objpos.X+obj._rectHull.Z));
            float dy=Math.Max(objpos.Y+(obj._rectHull.Y)-GlobalPosition.Y,GlobalPosition.Y-(objpos.Y+obj._rectHull.W));
            if(dx>=0 && dy>=0)
            {
                if(Mathf.Sqrt(dx*dx + dy*dy)>_maxRadius) return false;
            }
        }
        else if(!obj.UseRectHull)
        {
            float dx=Math.Max(GlobalPosition.X+(_rectHull.X)-objpos.X,objpos.X-(GlobalPosition.X+_rectHull.Z));
            float dy=Math.Max(GlobalPosition.Y+(_rectHull.Y)-objpos.Y,objpos.Y-(GlobalPosition.Y+_rectHull.W));
            if(dx>=0 || dy>=0)
            {
                if(Mathf.Sqrt(dx*dx + dy*dy)>obj._maxRadius) return false;
            }
        }
		return true;
    }

	/*
	선분List의 크기가 1이거나 마지막 선분의 경우, 선분과 선분사이의 충돌 대신 점과 선분, 점과 점 사이의 충돌을 수행합니다.
	기본적으로 HitTest에서는 SegmentToSegment만을 호출하며, 충돌검사 대상이나 다신이 점일 경우 PointToSegment, PointToPoint를 수행합니다.
	*/
    private Collision PointToPoint(int index, Collider obj, int objIndex)
    {
		Vector3 srcPosition = GetSegmentGlobalPosition(this,index);
		Vector3 objPosition = GetSegmentGlobalPosition(obj,objIndex);

 		Vector3 closet = objPosition - srcPosition;
		float distance = closet.Length();

		return distance < Width+obj.Width ? new Collision(index,obj,objIndex,closet - closet.Normalized()*obj.Width) : null;
    }
    private Collision PointToSegment(int index, Collider obj, int objIndex)
    {
        if(obj._segments.Count-1==objIndex)return PointToPoint(index, obj, objIndex);

		Vector3 srcPosition = GetSegmentGlobalPosition(this,index);
		Vector3 objPosition = GetSegmentGlobalPosition(obj,objIndex) - srcPosition;
		Vector3 objPositionEnd = GetSegmentGlobalPosition(obj,objIndex+1) - srcPosition;

        Vector3 closet =  Geometry3D.GetClosestPointToSegment(srcPosition, objPosition, objPositionEnd);
		float distance = closet.Length();

		return distance < Width+obj.Width ? new Collision(index,obj,objIndex,closet - closet.Normalized()*obj.Width) : null;
    }
    private Collision SegmentToSegment(int index, Collider obj, int objIndex)
    {
        if(_segments.Count-1==index)return PointToSegment(index,obj,objIndex);
        if(obj._segments.Count-1==objIndex)return obj.PointToSegment(objIndex,this,index);

		Vector3 srcPosition = GetSegmentGlobalPosition(this,index);
		Vector3 srcPositionEnd = GetSegmentGlobalPosition(this,index+1);
		Vector3 objPosition = GetSegmentGlobalPosition(obj,objIndex) - srcPosition;
		Vector3 objPositionEnd = GetSegmentGlobalPosition(obj,objIndex+1) - srcPosition;
		
		Vector3[] closetPoints =  Geometry3D.GetClosestPointsBetweenSegments(srcPosition, srcPositionEnd, objPosition, objPositionEnd);
		Vector3 closet = closetPoints[1] - closetPoints[0];
		float distance = closet.Length();

		return distance < Width+obj.Width ? new Collision(index,obj,objIndex,closet - closet.Normalized()*obj.Width) : null;
    }

	/*
	ConvexHull을 계산할때 rotation에 따라 선분이 돌아가는 경우도 계산합니다.
	*/
    private void SetConvexHull()
    {
		_rectHull = Vector4.Zero;
		_maxRadius = 0.0f;
		int i = 0;
		foreach(Segment segment in _segments)
        {
            if(!segment.Enable)continue;
            Vector3 position = GetSegmentGlobalPosition(this,i) - GlobalPosition;
			
			if(UseRectHull)
        	{
				if(position.X+Width>_rectHull.Z)_rectHull.Z=position.X+Width;
				if(position.X-Width<_rectHull.X)_rectHull.X=position.X-Width;
				if(position.Y+Width>_rectHull.W)_rectHull.W=position.Y+Width;
				if(position.Y-Width<_rectHull.Y)_rectHull.Y=position.Y-Width;

        	}
        	else if(position.Length()+Width>_maxRadius)
        	{
            	_maxRadius=position.Length()+Width;
        	}
			i++;
        }
    }
}
