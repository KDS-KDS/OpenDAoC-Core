using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    //public class MimicSorcerer : MimicNPC
    //{
    //    public MimicSorcerer(byte level) : base(new ClassSorcerer(), level)
    //    { }
    //}

    public class SorcererSpec : MimicSpec
    {
        public SorcererSpec(eSpecType spec)
        {
            SpecName = "SorcererSpec";

            WeaponOneType = eObjectType.Staff;
            Is2H = true;

            var randVariance = spec switch
            {
                eSpecType.MindSorc => Util.Random(0, 3),
                eSpecType.BodySorc => Util.Random(4, 6),
                _ => Util.Random(6),
            };
                    
            switch (randVariance)
            {
                case 0:
                case 1:
                SpecType = eSpecType.MindSorc;
                Add(Specs.Matter_Magic, 8, 0.0f);
                Add(Specs.Body_Magic, 30, 0.6f);
                Add(Specs.Mind_Magic, 44, 0.8f);
                break;

                case 2:
                SpecType = eSpecType.MindSorc;
                Add(Specs.Matter_Magic, 26, 0.5f);
                Add(Specs.Body_Magic, 5, 0.0f);
                Add(Specs.Mind_Magic, 47, 0.8f);
                break;

                case 3:
                SpecType = eSpecType.MindSorc;
                Add(Specs.Matter_Magic, 5, 0.0f);
                Add(Specs.Body_Magic, 22, 0.6f);
                Add(Specs.Mind_Magic, 49, 0.8f);
                break;

                case 4:
                SpecType = eSpecType.BodySorc;
                Add(Specs.Matter_Magic, 8, 0.0f);
                Add(Specs.Body_Magic, 40, 0.6f);
                Add(Specs.Mind_Magic, 36, 0.8f);
                break;

                case 5:
                SpecType = eSpecType.BodySorc;
                Add(Specs.Matter_Magic, 24, 0.6f);
                Add(Specs.Body_Magic, 48, 0.8f);
                Add(Specs.Mind_Magic, 6, 0.1f);
                break;

                case 6:
                SpecType = eSpecType.BodySorc;
                Add(Specs.Matter_Magic, 6, 0.0f);
                Add(Specs.Body_Magic, 45, 0.8f);
                Add(Specs.Mind_Magic, 29, 0.6f);
                break;
            }
        }
    }
}