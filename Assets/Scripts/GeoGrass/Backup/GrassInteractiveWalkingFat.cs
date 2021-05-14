using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WalkingFat;

namespace WalkingFat
{
    //[ExecuteInEditMode]
    public class DynamicGrass : MonoBehaviour
    {
        public float effectRadius = 2f;

        public GameObject grassModel;
        public Material grassMat;

        public float grassAreaWidth = 8f;
        public float grassAreaLength = 6f;
        public float grassDensity = 5f;
        public float grassSize = 1f;
        public float grassHeight = 1f;

        public float windAngle = 0f;
        public float windStrength = 1f;
        private Vector2 windDir = new Vector2(0, 0);

        public Transform[] obstacles;
        private Vector4[] obstaclePositions = new Vector4[100];

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;


        // check grass size change
        private float prevGrassAreaWidth;
        private float prevGrassAreaLength;
        private float prevGrassSize;
        private float prevGrassHeight;
        private GameObject prevGrassModel;

        // Use this for initialization
        void Start()
        {
            meshFilter = gameObject.AddComponent<MeshFilter>() as MeshFilter;
            meshRenderer = gameObject.AddComponent<MeshRenderer>() as MeshRenderer;

            meshRenderer.material = grassMat;

            InitializeGrass();

            prevGrassAreaWidth = grassAreaWidth;
            prevGrassAreaLength = grassAreaLength;
            prevGrassSize = grassSize;
            prevGrassHeight = grassHeight;
            prevGrassModel = grassModel;
        }

        // Update is called once per frame
        void Update()
        {
            // send data to grass shader
            for (int n = 0; n < obstacles.Length; n++)
            {
                obstaclePositions[n] = obstacles[n].position;
            }

            Shader.SetGlobalFloat("_PositionArray", obstacles.Length);
            Shader.SetGlobalVectorArray("_ObstaclePositions", obstaclePositions);

            Shader.SetGlobalVector("_ObstaclePosition", obstaclePositions[0]); // for H5

            Shader.SetGlobalFloat("_EffectRadius", effectRadius);

            windDir = GetWindDir(windAngle);

            Shader.SetGlobalFloat("_WindDirectionX", windDir.x);
            Shader.SetGlobalFloat("_WindDirectionZ", windDir.y);
            Shader.SetGlobalFloat("_WindStrength", windStrength);

            float shakeBending = Mathf.Lerp(0.5f, 2f, windStrength);
            Shader.SetGlobalFloat("_ShakeBending", shakeBending);

            // check grass size change
            if (prevGrassAreaWidth != grassAreaWidth || prevGrassAreaLength != grassAreaLength
                || prevGrassSize != grassSize || prevGrassHeight != grassHeight || prevGrassModel != grassModel)
            {
                InitializeGrass();

                prevGrassAreaWidth = grassAreaWidth;
                prevGrassAreaLength = grassAreaLength;
                prevGrassSize = grassSize;
                prevGrassHeight = grassHeight;
                prevGrassModel = grassModel;
            }
        }

        void InitializeGrass()
        {
            int tempGrassNumW = Mathf.FloorToInt(grassAreaWidth * grassDensity);
            int tempGrassNumL = Mathf.FloorToInt(grassAreaLength * grassDensity);

            float tempPosInterval = 1f / grassDensity;

            float tempPosStartX = grassAreaWidth * -0.5f;
            float tempPosStartZ = grassAreaLength * -0.5f;

            Vector3[] tempGrassPosList = new Vector3[tempGrassNumW * tempGrassNumL];
            GameObject[] temgGrassObjList = new GameObject[tempGrassNumW * tempGrassNumL];

            CombineInstance[] combine = new CombineInstance[tempGrassNumW * tempGrassNumL];

            float tempPosOffset = tempPosInterval * 0.3f;

            for (int w = 0; w < tempGrassNumW; w++)
            {
                for (int l = 0; l < tempGrassNumL; l++)
                {

                    tempGrassPosList[w + l * tempGrassNumW] = new Vector3(
                        tempPosStartX + w * tempPosInterval,
                        transform.position.y,
                        tempPosStartZ + l * tempPosInterval
                    ) + new Vector3(
                        Random.Range(-tempPosOffset, tempPosOffset),
                        0,
                        Random.Range(-tempPosOffset, tempPosOffset)
                    );

                    /*temgGrassObjList[w + l * tempGrassNumW] = Tool_Spawn.SpawnObj(
                        true, grassModel, gameObject, "Grass_" + (w + l * tempGrassNumW).ToString(),
                        tempGrassPosList[w + l * tempGrassNumW], new Vector3(0, Random.Range(0, 360), 0), false
                    );*/  // 这段不要注释
                    temgGrassObjList[w + l * tempGrassNumW].transform.localScale = new Vector3(grassSize, Random.Range(0.7f, 1.3f) * grassHeight, grassSize);

                    MeshFilter mf = temgGrassObjList[w + l * tempGrassNumW].GetComponent<MeshFilter>() as MeshFilter;
                    combine[w + l * tempGrassNumW].mesh = mf.sharedMesh;
                    combine[w + l * tempGrassNumW].transform = temgGrassObjList[w + l * tempGrassNumW].transform.localToWorldMatrix;
                    temgGrassObjList[w + l * tempGrassNumW].SetActive(false);
                }
            }

            meshFilter.mesh = new Mesh();
            meshFilter.mesh.CombineMeshes(combine);
            transform.gameObject.active = true;

            for (int n = 0; n < tempGrassPosList.Length; n++)
            {
                Destroy(temgGrassObjList[n]);
            }
        }

        Vector2 GetWindDir(float degree)
        {
            Vector2 dir = new Vector2(0, 0);

            float radian = degree * Mathf.Deg2Rad;
            dir = new Vector2(Mathf.Cos(radian), Mathf.Sin(radian));

            return dir.normalized;
        }
    }
}